using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using InterviewAudit.Domain.Interfaces;

namespace InterviewAudit.Infrastructure.Llm
{
    public class LlmSettings
    {
        public bool UseMock { get; set; }
        public string ActiveProvider { get; set; } = string.Empty; // OpenAI, Claude, etc.
        public string Model { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public System.Collections.Generic.List<string> GroqApiKeys { get; set; } = new System.Collections.Generic.List<string>();
        public int GroqRateLimitResetMinutes { get; set; } = 60;
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
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

            if (string.Equals(_settings.ActiveProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<OpenAiLlmService>();
            }

            if (string.Equals(_settings.ActiveProvider, "Claude", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<ClaudeLlmService>();
            }

            if (string.Equals(_settings.ActiveProvider, "Gemini", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<GeminiLlmService>();
            }

            if (string.Equals(_settings.ActiveProvider, "Groq", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<GroqLlmService>();
            }

            if (string.Equals(_settings.ActiveProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
            {
                return _serviceProvider.GetRequiredService<OllamaLlmService>();
            }

            throw new NotSupportedException($"LLM Provider '{_settings.ActiveProvider}' is not supported.");
        }
    }
}
