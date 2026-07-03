using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;
using InterviewAudit.Application.Services;

namespace InterviewAudit.Infrastructure.Graph
{
    public class MockGraphService : IGraphService
    {
        private readonly string _mockDataFolder;
        private readonly ILogger<MockGraphService> _logger;

        public MockGraphService(IOptions<StorageOptions> storageOptions, ILogger<MockGraphService> logger)
        {
            _mockDataFolder = storageOptions.Value.MockDataFolder;
            _logger = logger;
        }

        public async Task<GraphEventRetrievalResult> ProcessOrganizerMeetingsAsync(string userId, Func<List<Meeting>, Task<bool>> pageProcessor, CancellationToken cancellationToken)
        {
            var result = new GraphEventRetrievalResult { ProcessingDuration = TimeSpan.Zero };
            var start = DateTime.UtcNow;

            _logger.LogInformation("MockGraph: Fetching meetings for user {UserId} from mock data...", userId);
            
            string filePath = Path.Combine(_mockDataFolder, $"meetings_{userId}.json");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("MockGraph: Mock data file not found at {Path}. Generating default mock meetings...", filePath);
                await GenerateDefaultMockDataAsync(userId, cancellationToken);
            }

            try
            {
                string json = await File.ReadAllTextAsync(filePath, cancellationToken);
                var meetings = JsonSerializer.Deserialize<List<Meeting>>(json) ?? new List<Meeting>();
                
                result.TotalEventsRetrieved = meetings.Count;
                result.TotalPagesProcessed = 1;
                
                if (meetings.Any())
                {
                    await pageProcessor(meetings);
                }

                result.IsSuccess = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MockGraph: Failed to load mock meetings from {Path}", filePath);
                result.IsSuccess = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                result.ProcessingDuration = DateTime.UtcNow - start;
            }

            return result;
        }

        public async Task<List<Transcript>> GetTranscriptsAsync(string userId, string meetingId, string joinUrl, CancellationToken cancellationToken)
        {
            _logger.LogInformation("MockGraph: Retrieving transcript for meeting {MeetingId} (Organizer: {UserId}) from mock data...", meetingId, userId);
            
            var list = new List<Transcript>();
            string filePath = Path.Combine(_mockDataFolder, $"transcript_{meetingId}.txt");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("MockGraph: Mock transcript file not found at {Path}. Generating default mock transcript...", filePath);
                await GenerateDefaultMockTranscriptAsync(meetingId, cancellationToken);
            }

            try
            {
                string text = await File.ReadAllTextAsync(filePath, cancellationToken);
                list.Add(new Transcript
                {
                    Id = $"tx-{meetingId}",
                    MeetingId = meetingId,
                    FileName = $"transcript_{meetingId}.txt",
                    Content = text,
                    CreatedDateTime = DateTimeOffset.Now.AddDays(-1)
                });
                return list;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MockGraph: Failed to read mock transcript from {Path}", filePath);
                return list;
            }
        }

        private async Task GenerateDefaultMockDataAsync(string userId, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(_mockDataFolder))
                {
                    Directory.CreateDirectory(_mockDataFolder);
                }

                var defaultMeetings = new List<Meeting>
                {
                    new Meeting
                    {
                        Id = "mt-001",
                        Subject = "Senior .NET 10 Developer Interview - John Doe",
                        Description = "Candidate Name: John Doe\r\nInterviewer Name: Jane Smith\r\n\r\nJob Description:\r\nWe are looking for a Senior .NET 10 Engineer with strong experience in C#, ASP.NET Core, SQL Server, and Microsoft Graph API. Knowledge of clean architecture and AI integration is highly preferred.",
                        OrganizerName = "jane.smith@example.com",
                        StartDateTime = DateTimeOffset.Now.AddDays(-1).AddHours(-2),
                        EndDateTime = DateTimeOffset.Now.AddDays(-1).AddHours(-1),
                        Attendees = new List<Attendee>
                        {
                            new Attendee { Name = "Jane Smith", Email = "jane.smith@example.com", Role = "Organizer" },
                            new Attendee { Name = "John Doe", Email = "john.doe@external.com", Role = "External" }
                        }
                    },
                    new Meeting
                    {
                        Id = "mt-002",
                        Subject = "Angular Developer Interview - Mark Miller",
                        Description = "Candidate Name: Mark Miller\r\nInterviewer Name: Jane Smith\r\n\r\nNotice: This meeting is missing the JD field to test the fallback skip logic.",
                        OrganizerName = "jane.smith@example.com",
                        StartDateTime = DateTimeOffset.Now.AddDays(-1),
                        EndDateTime = DateTimeOffset.Now.AddDays(-1).AddMinutes(30),
                        Attendees = new List<Attendee>
                        {
                            new Attendee { Name = "Jane Smith", Email = "jane.smith@example.com", Role = "Organizer" },
                            new Attendee { Name = "Mark Miller", Email = "mark.m@external.com", Role = "External" }
                        }
                    },
                    new Meeting
                    {
                        Id = "mt-003",
                        Subject = "Cloud Architect Technical Assessment",
                        Description = "Job Description:\r\nDesign and implement Azure cloud landing zones, DevOps pipelines, and IaC using Terraform. Must have Azure Solution Architect Certification.\r\n\r\nNotice: This meeting has candidate and interviewer names missing in the description to test the LLM pre-analysis attendee extraction.",
                        OrganizerName = "bob.jones@example.com",
                        StartDateTime = DateTimeOffset.Now.AddDays(-2),
                        EndDateTime = DateTimeOffset.Now.AddDays(-2).AddHours(1),
                        Attendees = new List<Attendee>
                        {
                            new Attendee { Name = "Bob Jones", Email = "bob.jones@example.com", Role = "Organizer" },
                            new Attendee { Name = "Alice Wright", Email = "alice.wright@external.com", Role = "External" }
                        }
                    }
                };

                string filePath = Path.Combine(_mockDataFolder, $"meetings_{userId}.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(defaultMeetings, options);
                await File.WriteAllTextAsync(filePath, json, cancellationToken);
                _logger.LogInformation("MockGraph: Generated default mock meetings at {Path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MockGraph: Failed to generate default mock meetings");
            }
        }

        private async Task GenerateDefaultMockTranscriptAsync(string meetingId, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(_mockDataFolder))
                {
                    Directory.CreateDirectory(_mockDataFolder);
                }

                string transcriptText = "";

                if (meetingId == "mt-001")
                {
                    transcriptText = @"WEBVTT
Language: en-US

00:00:00.100 --> 00:00:05.200
<v Jane Smith>Hello John, thanks for joining today. How are you doing?</v>

00:00:05.500 --> 00:00:09.100
<v John Doe>Hi Jane! I am doing great, thank you. Glad to be here.</v>

00:00:09.600 --> 00:00:15.500
<v Jane Smith>Excellent. Today we're interviewing you for the Senior .NET 10 Developer role. Let's start with C# basics. What is dependency injection and how do you implement it in .NET 10?</v>

00:00:16.000 --> 00:00:30.000
<v John Doe>Dependency injection is a design pattern used to achieve Inversion of Control between classes. In .NET 10, it is built-in. We register services in the Program.cs file using builders.Services.AddTransient or AddScoped or AddSingleton, and inject them via constructor injection.</v>

00:00:30.500 --> 00:00:35.000
<v Jane Smith>That is correct. What is the difference between scoped and singleton lifetime?</v>

00:00:35.500 --> 00:00:48.000
<v John Doe>A singleton service has a single instance created once for the lifetime of the application. A scoped service is created once per client request or scope. For example, in ASP.NET Core, it's created once per HTTP request.</v>

00:00:48.500 --> 00:00:54.000
<v Jane Smith>Very good. Have you worked with Clean Architecture before?</v>

00:00:54.500 --> 00:01:10.000
<v John Doe>Yes, in my last project, we divided the project into Domain, Application, Infrastructure, and Web layers. The Domain layer had no external dependencies, and all external integrations were hidden behind interfaces implemented in the Infrastructure layer.</v>

00:01:10.500 --> 00:01:15.000
<v Jane Smith>Excellent. That's exactly what we use. Thank you, John.</v>";
                }
                else if (meetingId == "mt-003")
                {
                    // This is the one with missing names in description (Bob Jones is interviewer, Alice Wright is candidate)
                    transcriptText = @"WEBVTT

00:00:00.100 --> 00:00:04.500
<v Bob Jones>Welcome to the Technical Assessment. Let's get started. Alice, could you tell me about your Terraform experience?</v>

00:00:05.000 --> 00:00:20.000
<v Alice Wright>Sure, Bob. I've designed Azure cloud landing zones using Terraform modules. I write reusable modules for virtual networks, App Services, and key vaults, managing state files remotely in Azure Blob Storage with state locking.</v>

00:00:20.500 --> 00:00:25.000
<v Bob Jones>Great. What Azure certifications do you hold?</v>

00:00:25.500 --> 00:00:32.000
<v Alice Wright>I hold the Azure Solutions Architect Expert certification, which covers design patterns, security, and infrastructure scale.</v>";
                }
                else
                {
                    transcriptText = @"WEBVTT

00:00:01.000 --> 00:00:05.000
<v Interviewer>Hello, let's start the interview.</v>

00:00:06.000 --> 00:00:10.000
<v Candidate>Hello! Glad to join.</v>";
                }

                string filePath = Path.Combine(_mockDataFolder, $"transcript_{meetingId}.txt");
                await File.WriteAllTextAsync(filePath, transcriptText, cancellationToken);
                _logger.LogInformation("MockGraph: Generated default mock transcript at {Path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MockGraph: Failed to generate default mock transcript");
            }
        }
    }
}


