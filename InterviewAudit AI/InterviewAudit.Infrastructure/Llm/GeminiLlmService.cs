using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Infrastructure.Llm
{
    public class GeminiLlmService : ILlmService
    {
        public bool IsAvailable() => _apiKeyManager.HasAvailableKeys("Gemini");
        private readonly ILlmApiKeyManager _apiKeyManager;
        private readonly string _modelName;
        private readonly ILogger<GeminiLlmService> _logger;
        private static readonly HttpClient HttpClient = new HttpClient();

        public GeminiLlmService(ILlmApiKeyManager apiKeyManager, string modelName, ILogger<GeminiLlmService> logger)
        {
            _apiKeyManager = apiKeyManager;
            _modelName = modelName;
            _logger = logger;
        }

        public async Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Gemini: Initiating quality audit report generation using model {Model}...", _modelName);

            string fullPrompt = promptTemplate
                .Replace("{{JD}}", jd)
                .Replace("{{TRANSCRIPT}}", transcript)
                .Replace("<PASTE JD HERE>", jd)
                .Replace("<PASTE INTERVIEW TRANSCRIPT HERE>", transcript);

            return await CallGeminiApiAsync(fullPrompt, 4000, cancellationToken);
        }

        public async Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            return await CallGeminiApiAsync(prompt, maxTokens, cancellationToken);
        }

        public async Task<(string CandidateName, string InterviewerName)> ExtractAttendeesAsync(List<Attendee> attendees, string transcriptSample, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Gemini: Extracting candidate and interviewer names using model {Model}...", _modelName);

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

            try
            {
                string responseText = await CallGeminiApiAsync(extractionPrompt, 1000, cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<AttendeeExtractionResult>(CleanJsonSnippet(responseText), options);

                if (result != null)
                {
                    _logger.LogInformation("Gemini: Extracted Candidate: {Candidate}, Interviewer: {Interviewer}", result.CandidateName, result.InterviewerName);
                    return (result.CandidateName ?? string.Empty, result.InterviewerName ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini: Failed to extract attendees using model {Model}.", _modelName);
            }

            return (string.Empty, string.Empty);
        }

        private async Task<string> CallGeminiApiAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = maxTokens
                }
            };

            string jsonPayload = JsonSerializer.Serialize(payload);

            int maxRetries = 5;
            int retryDelayMs = 15000; // start with 15 seconds for free tier rate limits

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                string activeKey = _apiKeyManager.GetNextAvailableKey("Gemini");
                if (string.IsNullOrWhiteSpace(activeKey))
                {
                    _logger.LogError("Gemini API: No available API keys. All keys are currently exhausted.");
                    throw new InvalidOperationException("All Gemini API keys are exhausted. Cannot proceed.");
                }

                string url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={activeKey}";

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                try
                {
                    var response = await HttpClient.SendAsync(request, cancellationToken);
                    string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            _logger.LogWarning("Gemini API Rate limit exhausted (429) for current key. Attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                            _apiKeyManager.MarkKeyExhausted("Gemini", activeKey);
                            
                            if (attempt == maxRetries)
                            {
                                _logger.LogError("Gemini API Error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                                response.EnsureSuccessStatusCode();
                            }
                            continue;
                        }
                        else if ((int)response.StatusCode >= 500)
                        {
                            _logger.LogWarning("Gemini API Server error (Attempt {Attempt}/{MaxRetries}): {StatusCode}", attempt, maxRetries, response.StatusCode);
                            
                            if (attempt == maxRetries)
                            {
                                _logger.LogError("Gemini API Error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                                response.EnsureSuccessStatusCode();
                            }
                            
                            if (response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue)
                            {
                                await Task.Delay(response.Headers.RetryAfter.Delta.Value, cancellationToken);
                            }
                            else
                            {
                                await Task.Delay(retryDelayMs, cancellationToken);
                            }
                            
                            retryDelayMs *= 2;
                            continue;
                        }

                        _logger.LogError("Gemini API Error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                        response.EnsureSuccessStatusCode();
                    }

                    using var doc = JsonDocument.Parse(responseContent);
                    if (doc.RootElement.TryGetProperty("candidates", out var candidatesElement) && candidatesElement.GetArrayLength() > 0)
                    {
                        var candidate = candidatesElement[0];
                        if (candidate.TryGetProperty("content", out var contentElement) && contentElement.TryGetProperty("parts", out var partsElement) && partsElement.GetArrayLength() > 0)
                        {
                            var part = partsElement[0];
                            if (part.TryGetProperty("text", out var textElement))
                            {
                                return textElement.GetString() ?? string.Empty;
                            }
                        }
                    }

                    _logger.LogWarning("Gemini API response did not contain the expected 'candidates[0].content.parts[0].text' format.");
                    return string.Empty;
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == maxRetries) throw;
                    _logger.LogWarning(ex, "Gemini API Transient network error (Attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
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
            }
            else if (input.StartsWith("```"))
            {
                input = input.Substring(3);
            }

            if (input.EndsWith("```"))
            {
                input = input.Substring(0, input.Length - 3);
            }

            return input.Trim();
        }

        private class AttendeeExtractionResult
        {
            public string? CandidateName { get; set; }
            public string? InterviewerName { get; set; }
        }
    }
}
