using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Infrastructure.Llm
{
    public class GroqLlmService : ILlmService
    {
        public bool IsAvailable() => _apiKeyManager.HasAvailableKeys("Groq");
        private readonly ILlmApiKeyManager _apiKeyManager;
        private readonly string _modelName;
        private readonly ILogger<GroqLlmService> _logger;
        private static readonly HttpClient HttpClient = new HttpClient();

        public GroqLlmService(ILlmApiKeyManager apiKeyManager, string modelName, ILogger<GroqLlmService> logger)
        {
            _apiKeyManager = apiKeyManager;
            _modelName = modelName;
            _logger = logger;
        }

        public async Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Groq: Initiating quality audit report generation using model {Model}...", _modelName);

            string fullPrompt = promptTemplate
                .Replace("{{JD}}", jd)
                .Replace("{{TRANSCRIPT}}", transcript)
                .Replace("<PASTE JD HERE>", jd)
                .Replace("<PASTE INTERVIEW TRANSCRIPT HERE>", transcript);

            return await CallGroqApiAsync(fullPrompt, 2500, cancellationToken);
        }

        public async Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            return await CallGroqApiAsync(prompt, maxTokens, cancellationToken);
        }

        public async Task<(string CandidateName, string InterviewerName)> ExtractAttendeesAsync(List<Attendee> attendees, string transcriptSample, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Groq: Extracting candidate and interviewer names using model {Model}...", _modelName);

            string attendeesJson = JsonSerializer.Serialize(attendees);
            string extractionPrompt = $@"You are a meeting assistant. Analyze the following Teams meeting attendees and the beginning of the interview transcript. Identify who is the candidate (interviewee) and who is the interviewer.

Meeting Attendees:
{attendeesJson}

Transcript Sample:
{transcriptSample}

You MUST return a JSON object with exactly two keys: 'CandidateName' and 'InterviewerName'. Do not write anything else.
Example:
{{
  ""CandidateName"": ""John Doe"",
  ""InterviewerName"": ""Jane Smith""
}}";

            string responseText = await CallGroqApiAsync(extractionPrompt, 500, cancellationToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            try
            {
                var result = JsonSerializer.Deserialize<AttendeeExtractionResult>(CleanJsonSnippet(responseText), options);
                if (result != null)
                {
                    return (result.CandidateName ?? string.Empty, result.InterviewerName ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Groq: Failed to parse attendee extraction JSON. Response was: {Response}", responseText);
            }

            return (string.Empty, string.Empty);
        }

        private async Task<string> CallGroqApiAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            // Dynamic output reservation limits
            if (maxTokens < 1000) maxTokens = 1000;
            if (maxTokens > 2500) maxTokens = 2500;

            int inputTokens = prompt.Length / 4;
            int safetyBuffer = 500;
            int totalEstimatedTokens = inputTokens + maxTokens + safetyBuffer;
            
            _logger.LogInformation("Groq API Token Estimation -> Input Tokens: {Input}, Reserved Output: {Output}, Buffer: {Buffer}, Total: {Total}", inputTokens, maxTokens, safetyBuffer, totalEstimatedTokens);
            
            if (totalEstimatedTokens > 12000)
            {
                _logger.LogWarning("Groq API Token limit warning! Estimated tokens ({Total}) exceed configured hard limit of 12000. RequestEntityTooLarge may occur.", totalEstimatedTokens);
            }

            string url = "https://api.groq.com/openai/v1/chat/completions";
            
            var payload = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = maxTokens,
                temperature = 0.2
            };

            string jsonPayload = JsonSerializer.Serialize(payload);

            int maxRetries = 5;
            int retryDelayMs = 2000; 

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                string activeKey = _apiKeyManager.GetNextAvailableKey("Groq");
                if (string.IsNullOrWhiteSpace(activeKey))
                {
                    _logger.LogError("Groq API: No available API keys. All keys are currently exhausted.");
                    throw new InvalidOperationException("All Groq API keys are exhausted. Cannot proceed.");
                }

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", activeKey);

                try
                {
                    var response = await HttpClient.SendAsync(request, cancellationToken);
                    string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode == 429)
                        {
                            _logger.LogWarning("Groq API Rate limit exhausted (429) for current key. Attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                            _apiKeyManager.MarkKeyExhausted("Groq", activeKey);
                            
                            // Immediately retry with the next available key without waiting 
                            // (unless it was the last attempt)
                            if (attempt == maxRetries)
                            {
                                _logger.LogError("Groq API Error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                                response.EnsureSuccessStatusCode();
                            }
                            continue;
                        }
                        else if ((int)response.StatusCode >= 500)
                        {
                            _logger.LogWarning("Groq API Server error (Attempt {Attempt}/{MaxRetries}): {StatusCode}", attempt, maxRetries, response.StatusCode);
                            
                            if (attempt == maxRetries)
                            {
                                _logger.LogError("Groq API Error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                                response.EnsureSuccessStatusCode();
                            }
                            
                            await Task.Delay(retryDelayMs, cancellationToken);
                            retryDelayMs *= 2; 
                            continue;
                        }

                        _logger.LogError("Groq API Error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                        throw new InvalidOperationException($"Groq API Error: {response.StatusCode} - {responseContent}");
                    }

                    using var doc = JsonDocument.Parse(responseContent);
                    if (doc.RootElement.TryGetProperty("choices", out var choicesElement) && choicesElement.GetArrayLength() > 0)
                    {
                        var choice = choicesElement[0];
                        if (choice.TryGetProperty("message", out var messageElement) && messageElement.TryGetProperty("content", out var contentElement))
                        {
                            return contentElement.GetString() ?? string.Empty;
                        }
                    }

                    _logger.LogWarning("Groq API response did not contain the expected 'choices[0].message.content' format.");
                    return string.Empty;
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == maxRetries) throw;
                    _logger.LogWarning(ex, "Groq API Transient network error (Attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                    await Task.Delay(retryDelayMs, cancellationToken);
                    retryDelayMs *= 2;
                }
            }

            return string.Empty;
        }

        private string CleanJsonSnippet(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "{}";
            
            if (input.StartsWith("```json"))
            {
                input = input.Substring(7);
                if (input.EndsWith("```")) input = input.Substring(0, input.Length - 3);
            }
            else if (input.StartsWith("```"))
            {
                input = input.Substring(3);
                if (input.EndsWith("```")) input = input.Substring(0, input.Length - 3);
            }

            return input.Trim();
        }

        private class AttendeeExtractionResult
        {
            public string CandidateName { get; set; } = string.Empty;
            public string InterviewerName { get; set; } = string.Empty;
        }
    }
}








