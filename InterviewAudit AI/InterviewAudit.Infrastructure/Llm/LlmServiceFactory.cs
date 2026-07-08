using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using InterviewAudit.Domain.Interfaces;

namespace InterviewAudit.Infrastructure.Llm
{
    public class ProviderSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public List<string> ApiKeys { get; set; } = new List<string>();
        public string Model { get; set; } = string.Empty;
        public int RateLimitResetMinutes { get; set; } = 60;
    }

    public class OllamaSettings
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    public class GroqSettings : ProviderSettings
    {
    }

    public class LlmSettings
    {
        public bool UseMock { get; set; }
        public string ActiveProvider { get; set; } = string.Empty; // OpenAI, Claude, Groq, Ollama, FallbackPipeline, etc.
        public string Model { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public List<string> GroqApiKeys { get; set; } = new List<string>();
        public int GroqRateLimitResetMinutes { get; set; } = 60;
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

        // Fallback Pipeline Configurations
        public List<string> FallbackChain { get; set; } = new List<string>();
        public ProviderSettings OpenAiSettings { get; set; } = new ProviderSettings();
        public ProviderSettings ClaudeSettings { get; set; } = new ProviderSettings();
        public ProviderSettings GeminiSettings { get; set; } = new ProviderSettings();
        public OllamaSettings OllamaSettings { get; set; } = new OllamaSettings();
        public GroqSettings GroqSettings { get; set; } = new GroqSettings();
    }

    public class LlmServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly LlmSettings _settings;

        public LlmServiceFactory(IServiceProvider serviceProvider, IOptions<LlmSettings> settings)
        {
            _serviceProvider = serviceProvider;
            _settings = settings.Value;
        }

        public ILlmService GetLlmService()
        {
            if (_settings.UseMock)
            {
                return _serviceProvider.GetRequiredService<MockLlmService>();
            }

            if (string.Equals(_settings.ActiveProvider, "FallbackPipeline", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_settings.ActiveProvider, "Fallback", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<FallbackLlmService>();
            }

            return GetLlmServiceByName(_settings.ActiveProvider);
        }

        public ILlmService GetLlmServiceByName(string providerName)
        {
            if (string.Equals(providerName, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<OpenAiLlmService>();
            }

            if (string.Equals(providerName, "Claude", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<ClaudeLlmService>();
            }

            if (string.Equals(providerName, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<GeminiLlmService>();
            }

            if (string.Equals(providerName, "Groq", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<GroqLlmService>();
            }

            if (string.Equals(providerName, "Ollama", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<OllamaLlmService>();
            }

            if (string.Equals(providerName, "Mock", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<MockLlmService>();
            }

            throw new NotSupportedException($"LLM Provider '{providerName}' is not supported.");
        }
    }
}
