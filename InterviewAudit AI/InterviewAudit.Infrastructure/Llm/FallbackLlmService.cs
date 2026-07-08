using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Infrastructure.Llm
{
    public class FallbackLlmService : ILlmService
    {
        private readonly LlmServiceFactory _factory;
        private readonly LlmSettings _settings;
        private readonly ILogger<FallbackLlmService> _logger;

        public FallbackLlmService(
            LlmServiceFactory factory,
            IOptions<LlmSettings> settings,
            ILogger<FallbackLlmService> logger)
        {
            _factory = factory;
            _settings = settings.Value;
            _logger = logger;
        }

        public bool IsAvailable()
        {
            var chain = GetFallbackChain();
            return chain.Any(provider =>
            {
                try
                {
                    var service = _factory.GetLlmServiceByName(provider);
                    return service.IsAvailable();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Fallback Pipeline: Availability check failed for provider {Provider}. Error: {Message}", provider, ex.Message);
                    return false;
                }
            });
        }

        public async Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken)
        {
            return await ExecuteWithFallbackAsync(
                async (service) => await service.GenerateReportAsync(promptTemplate, jd, transcript, cancellationToken),
                "GenerateReportAsync"
            );
        }

        public async Task<(string CandidateName, string InterviewerName)> ExtractAttendeesAsync(List<Attendee> attendees, string transcriptSample, CancellationToken cancellationToken)
        {
            return await ExecuteWithFallbackAsync(
                async (service) => await service.ExtractAttendeesAsync(attendees, transcriptSample, cancellationToken),
                "ExtractAttendeesAsync"
            );
        }

        public async Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            return await ExecuteWithFallbackAsync(
                async (service) => await service.GenerateTextAsync(prompt, maxTokens, cancellationToken),
                "GenerateTextAsync"
            );
        }

        private List<string> GetFallbackChain()
        {
            if (_settings.FallbackChain != null && _settings.FallbackChain.Any())
            {
                return _settings.FallbackChain;
            }

            // Default fallback order if none configured
            return new List<string> { "Groq", "OpenAI", "Gemini", "Claude", "Ollama" };
        }

        private async Task<T> ExecuteWithFallbackAsync<T>(Func<ILlmService, Task<T>> action, string operationName)
        {
            var chain = GetFallbackChain();
            var exceptions = new List<Exception>();

            foreach (var provider in chain)
            {
                ILlmService service;
                try
                {
                    service = _factory.GetLlmServiceByName(provider);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Fallback Pipeline: Could not resolve service for provider {Provider}. Error: {Message}", provider, ex.Message);
                    exceptions.Add(ex);
                    continue;
                }

                if (!service.IsAvailable())
                {
                    _logger.LogInformation("Fallback Pipeline: Skipping provider {Provider} as it is not configured or unavailable.", provider);
                    continue;
                }

                _logger.LogInformation("Fallback Pipeline: Attempting {Operation} with provider {Provider}...", operationName, provider);

                try
                {
                    var result = await action(service);
                    _logger.LogInformation("Fallback Pipeline: Successfully executed {Operation} using provider {Provider}.", operationName, provider);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fallback Pipeline: Provider {Provider} failed execution of {Operation}.", provider, operationName);
                    exceptions.Add(ex);
                }
            }

            _logger.LogError("Fallback Pipeline: All configured providers failed for operation {Operation}.", operationName);
            throw new AggregateException($"Fallback Pipeline failed all attempts for {operationName}.", exceptions);
        }
    }
}
