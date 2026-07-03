using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Application.Services;
using InterviewAudit.Infrastructure.Graph;
using InterviewAudit.Infrastructure.Llm;
using InterviewAudit.Infrastructure.Persistence;

namespace InterviewAudit.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("groups.json", optional: true, reloadOnChange: true)
                .AddJsonFile("llm.json", optional: true, reloadOnChange: true)
                .AddJsonFile("prompts.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            // Intercept `--test` command line argument for Graph connectivity diagnostics
            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                RunDiagnosticTest(configuration);
                return;
            }

            try
            {
                Log.Information("Starting Windows Service Host...");
                CreateHostBuilder(args, configuration).Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static void RunDiagnosticTest(IConfiguration configuration)
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("MICROSOFT GRAPH API DIAGNOSTIC TEST");
            Console.WriteLine("==================================================");

            var graphSettings = new GraphSettings();
            configuration.GetSection("GraphSettings").Bind(graphSettings);

            Console.WriteLine($"Tenant ID:     {graphSettings.TenantId}");
            Console.WriteLine($"Client ID:     {graphSettings.ClientId}");
            Console.WriteLine($"Client Secret: [Masked: {graphSettings.ClientSecret.Length} chars]");
            Console.WriteLine($"Use Mock:      {graphSettings.UseMock}");

            try
            {
                var credential = new ClientSecretCredential(graphSettings.TenantId, graphSettings.ClientId, graphSettings.ClientSecret);
                Console.WriteLine("\n[1/4] Acquiring Access Token...");
                string scope = string.IsNullOrEmpty(graphSettings.TokenScope) 
                    ? "https://graph.microsoft.com/.default" 
                    : graphSettings.TokenScope;
                var tokenRequestContext = new TokenRequestContext(new[] { scope });
                var tokenResult = credential.GetToken(tokenRequestContext);

                string token = tokenResult.Token;
                string maskedToken = token.Substring(0, Math.Min(15, token.Length)) + "..." + token.Substring(Math.Max(0, token.Length - 15));
                
                Console.WriteLine("✔ Token acquired successfully!");
                Console.WriteLine($"Token Details: Masked={maskedToken}");
                Console.WriteLine($"Expires On:    {tokenResult.ExpiresOn}");

                var client = new GraphServiceClient(credential, new[] { scope });

                // Check [2/4] Groups Endpoint Permission
                Console.WriteLine("\n[2/4] Testing Group.Read.All permission (Groups list)...");
                try
                {
                    var groupsTask = client.Groups.GetAsync();
                    groupsTask.Wait();
                    var groups = groupsTask.Result;
                    Console.WriteLine($"✔ Group.Read.All: GRANTED. (Retrieved {groups?.Value?.Count ?? 0} groups)");
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    if (inner.Message.Contains("403") || inner.Message.Contains("Forbidden") || inner.Message.Contains("privileges"))
                    {
                        Console.WriteLine("❌ Group.Read.All: MISSING. (Status: 403 Forbidden - Insufficient privileges)");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Group.Read.All: ERROR ({inner.Message})");
                    }
                }

                // Check [3/4] Users Endpoint Permission
                Console.WriteLine("\n[3/4] Testing User.Read.All permission (Users list)...");
                string? testUserId = null;
                string? testUserPrincipalName = null;
                try
                {
                    var usersTask = client.Users.GetAsync();
                    usersTask.Wait();
                    var users = usersTask.Result;
                    Console.WriteLine($"✔ User.Read.All: GRANTED. (Retrieved {users?.Value?.Count ?? 0} users)");
                    
                    var firstUser = users?.Value?.FirstOrDefault();
                    if (firstUser != null)
                    {
                        testUserId = firstUser.Id;
                        testUserPrincipalName = firstUser.UserPrincipalName;
                    }
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException ?? ex;
                    if (inner.Message.Contains("403") || inner.Message.Contains("Forbidden") || inner.Message.Contains("privileges"))
                    {
                        Console.WriteLine("❌ User.Read.All: MISSING. (Status: 403 Forbidden - Insufficient privileges)");
                    }
                    else
                    {
                        Console.WriteLine($"❌ User.Read.All: ERROR ({inner.Message})");
                    }
                }

                // Check [4/4] OnlineMeetings & Transcripts Permissions
                if (string.IsNullOrEmpty(testUserId))
                {
                    Console.WriteLine("\n[4/4] Testing OnlineMeetings.Read.All & OnlineMeetingTranscript.Read.All...");
                    Console.WriteLine("⚠ Skip online meetings check: No test user found (User.Read.All permission missing).");
                }
                else
                {
                    string targetUser = testUserPrincipalName ?? testUserId;
                    Console.WriteLine($"\n[4/4] Testing OnlineMeetings & Transcripts via test user '{targetUser}'...");
                    
                    // A. OnlineMeetings.Read.All
                    try
                    {
                        Console.WriteLine("Checking OnlineMeetings.Read.All via dummy meeting ID lookup...");
                        var meetingTask = client.Users[testUserId].OnlineMeetings["dummy-meeting-id"].GetAsync();
                        meetingTask.Wait();
                        Console.WriteLine("✔ OnlineMeetings.Read.All: GRANTED. (Status: 200/404 - Dummy lookup reached endpoint)");
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        if (inner.Message.Contains("404") || inner.Message.Contains("NotFound"))
                        {
                            Console.WriteLine("✔ OnlineMeetings.Read.All: GRANTED. (Status: 404 Not Found - Dummy lookup reached endpoint)");
                        }
                        else if (inner.Message.Contains("403") || inner.Message.Contains("Forbidden") || inner.Message.Contains("privileges"))
                        {
                            Console.WriteLine("❌ OnlineMeetings.Read.All: MISSING. (Status: 403 Forbidden)");
                        }
                        else
                        {
                            Console.WriteLine($"❌ OnlineMeetings.Read.All: ERROR ({inner.Message})");
                        }
                    }

                    // B. OnlineMeetingTranscript.Read.All
                    try
                    {
                        Console.WriteLine("Checking OnlineMeetingTranscript.Read.All via dummy transcript ID lookup...");
                        var transcriptTask = client.Users[testUserId].OnlineMeetings["dummy-meeting-id"].Transcripts.GetAsync();
                        transcriptTask.Wait();
                        Console.WriteLine("✔ OnlineMeetingTranscript.Read.All: GRANTED. (Status: 200/404 - Dummy lookup reached endpoint)");
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        if (inner.Message.Contains("404") || inner.Message.Contains("NotFound"))
                        {
                            Console.WriteLine("✔ OnlineMeetingTranscript.Read.All: GRANTED. (Status: 404 Not Found - Dummy lookup reached endpoint)");
                        }
                        else if (inner.Message.Contains("403") || inner.Message.Contains("Forbidden") || inner.Message.Contains("privileges"))
                        {
                            Console.WriteLine("❌ OnlineMeetingTranscript.Read.All: MISSING. (Status: 403 Forbidden)");
                        }
                        else
                        {
                            Console.WriteLine($"❌ OnlineMeetingTranscript.Read.All: ERROR ({inner.Message})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n❌ Diagnostic Test Handshake FAILED!");
                var inner = ex.InnerException ?? ex;
                Console.WriteLine($"Authentication Error: {inner.Message}");
            }
            Console.WriteLine("==================================================");
        }

        public static IHostBuilder CreateHostBuilder(string[] args, IConfiguration configuration) =>
            Host.CreateDefaultBuilder(args)
                .UseWindowsService() // Enables running as a Windows Service when deployed
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    // Bind Configurations
                    services.Configure<SchedulerOptions>(configuration.GetSection("Scheduler"));
                    services.Configure<StorageOptions>(configuration.GetSection("Storage"));
                    services.Configure<GraphSettings>(configuration.GetSection("GraphSettings"));
                    services.Configure<LlmSettings>(configuration.GetSection("LlmSettings"));
                    services.Configure<PromptOptions>(configuration.GetSection("PromptSettings"));
                    services.Configure<ParserOptions>(configuration.GetSection("ParserSettings"));
                    services.Configure<OrganizerOptions>(configuration.GetSection("GraphSettings"));
                    services.Configure<InterviewFilterSettings>(configuration.GetSection("InterviewFilterSettings"));

                    // Resolve Relative Paths
                    services.PostConfigure<StorageOptions>(options =>
                    {
                        options.JdFolder = ResolveRelativePath(options.JdFolder);
                        options.ReportsFolder = ResolveRelativePath(options.ReportsFolder);
                        options.StateFilePath = ResolveRelativePath(options.StateFilePath);
                        options.MockDataFolder = ResolveRelativePath(options.MockDataFolder);
                        options.TranscriptsFolder = ResolveRelativePath(options.TranscriptsFolder);
                    });

                    services.PostConfigure<PromptOptions>(options =>
                    {
                        options.PromptFilePath = ResolveRelativePath(options.PromptFilePath);
                    });

                    // Register Repositories
                    services.AddSingleton<IStateRepository, FileSystemStateRepository>();
                    services.AddSingleton<IReportRepository, FileSystemReportRepository>();

                    // Register Concrete Graph implementations
                    services.AddTransient<MicrosoftGraphService>();
                    services.AddTransient<MockGraphService>();

                    // Dynamic Graph Service registration based on config UseMock
                    services.AddTransient<IGraphService>(sp =>
                    {
                        var graphSettings = sp.GetRequiredService<IOptions<GraphSettings>>().Value;
                        return graphSettings.UseMock
                            ? sp.GetRequiredService<MockGraphService>()
                            : sp.GetRequiredService<MicrosoftGraphService>();
                    });

                    // Register Concrete LLM implementations
                    services.AddTransient<MockLlmService>();
                    
                    // OpenAI and Claude registrations with factory parameters
                    services.AddTransient(sp =>
                    {
                        var settings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
                        var logger = sp.GetRequiredService<ILogger<OpenAiLlmService>>();
                        return new OpenAiLlmService(settings.ApiKey, settings.Model, logger);
                    });

                    services.AddTransient(sp =>
                    {
                        var settings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
                        var logger = sp.GetRequiredService<ILogger<ClaudeLlmService>>();
                        return new ClaudeLlmService(settings.ApiKey, settings.Model, logger);
                    });

                    services.AddTransient(sp =>
                    {
                        var settings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
                        var logger = sp.GetRequiredService<ILogger<GeminiLlmService>>();
                        return new GeminiLlmService(settings.ApiKey, settings.Model, logger);
                    });

                    services.AddSingleton<IGroqApiKeyManager, GroqApiKeyManager>();

                    services.AddTransient(sp =>
                    {
                        var settings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
                        var logger = sp.GetRequiredService<ILogger<GroqLlmService>>();
                        var apiKeyManager = sp.GetRequiredService<IGroqApiKeyManager>();
                        return new GroqLlmService(apiKeyManager, settings.Model, logger);
                    });

                    services.AddTransient(sp =>
                    {
                        var settings = sp.GetRequiredService<IOptions<LlmSettings>>().Value;
                        var logger = sp.GetRequiredService<ILogger<OllamaLlmService>>();
                        return new OllamaLlmService(settings.OllamaBaseUrl, settings.Model, logger);
                    });

                    services.AddSingleton<LlmServiceFactory>();

                    // Dynamic LLM Service registration via Factory
                    services.AddTransient<ILlmService>(sp =>
                    {
                        var factory = sp.GetRequiredService<LlmServiceFactory>();
                        return factory.GetLlmService();
                    });

                    // Register Transcript Processing Services
                    services.AddTransient<ITranscriptAggregator, TranscriptAggregator>();
                    services.AddTransient<IJdSummarizerService, JdSummarizerService>();
                    services.AddTransient<IChunkedLlmProcessor, ChunkedLlmProcessor>();

                    // Register Orchestration Service
                    services.AddTransient<AuditScheduler>();

                    // Register Background Hosted Worker
                    services.AddHostedService<Worker>();
                });

        private static string ResolveRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (System.IO.Path.IsPathRooted(path))
            {
                return path;
            }

            return System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, path));
        }
    }
}


