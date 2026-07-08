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
    public class ClaudeLlmService : ILlmService
    {
        public bool IsAvailable() => _apiKeyManager.HasAvailableKeys("Claude");
        private readonly ILlmApiKeyManager _apiKeyManager;
        private readonly string _modelName;
        private readonly ILogger<ClaudeLlmService> _logger;
        private static readonly HttpClient HttpClient = new HttpClient();

        public ClaudeLlmService(ILlmApiKeyManager apiKeyManager, string modelName, ILogger<ClaudeLlmService> logger)
        {
            _apiKeyManager = apiKeyManager;
            _modelName = modelName;
            _logger = logger;
        }

        public async Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Claude: Initiating quality audit report generation using model {Model}...", _modelName);

            string fullPrompt = promptTemplate
                .Replace("{{JD}}", jd)
                .Replace("{{TRANSCRIPT}}", transcript)
                .Replace("<PASTE JD HERE>", jd)
                .Replace("<PASTE INTERVIEW TRANSCRIPT HERE>", transcript);

            return await CallClaudeApiInternalAsync(fullPrompt, 4000, cancellationToken);
        }

        public async Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            return await CallClaudeApiInternalAsync(prompt, maxTokens, cancellationToken);
        }

        public async Task<(string CandidateName, string InterviewerName)> ExtractAttendeesAsync(List<Attendee> attendees, string transcriptSample, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Claude: Extracting candidate and interviewer names using model {Model}...", _modelName);

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
                string responseText = await CallClaudeApiInternalAsync(extractionPrompt, 1000, cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<AttendeeExtractionResult>(CleanJsonSnippet(responseText), options);

                if (result != null)
                {
                    _logger.LogInformation("Claude: Extracted Candidate: {Candidate}, Interviewer: {Interviewer}", result.CandidateName, result.InterviewerName);
                    return (result.CandidateName ?? string.Empty, result.InterviewerName ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Claude: Failed to extract attendees using model {Model}.", _modelName);
            }

            return (string.Empty, string.Empty);
        }

        private async Task<string> CallClaudeApiInternalAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            int maxRetries = 5;
            int retryDelayMs = 2000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                string activeKey = _apiKeyManager.GetNextAvailableKey("Claude");
                if (string.IsNullOrWhiteSpace(activeKey))
                {
                    _logger.LogError("Claude API: No available API keys. All keys are currently exhausted.");
                    throw new InvalidOperationException("All Claude API keys are exhausted. Cannot proceed.");
                }

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
                request.Headers.Add("x-api-key", activeKey);
                request.Headers.Add("anthropic-version", "2023-06-01");

                var payload = new ClaudeRequest
                {
                    Model = _modelName,
                    MaxTokens = maxTokens,
                    Messages = new List<ClaudeMessage>
                    {
                        new ClaudeMessage { Role = "user", Content = prompt }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                try
                {
                    var response = await HttpClient.SendAsync(request, cancellationToken);
                    string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode == 429)
                        {
                            _logger.LogWarning("Claude API Rate limit exhausted (429) for current key. Attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                            _apiKeyManager.MarkKeyExhausted("Claude", activeKey);
                            if (attempt == maxRetries)
                            {
                                response.EnsureSuccessStatusCode();
                            }
                            continue;
                        }
                        else if ((int)response.StatusCode >= 500)
                        {
                            _logger.LogWarning("Claude API Server error (Attempt {Attempt}/{MaxRetries}): {StatusCode}", attempt, maxRetries, response.StatusCode);
                            if (attempt == maxRetries)
                            {
                                response.EnsureSuccessStatusCode();
                            }
                            await Task.Delay(retryDelayMs, cancellationToken);
                            retryDelayMs *= 2;
                            continue;
                        }

                        _logger.LogError("Claude API Error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                        throw new InvalidOperationException($"Claude API Error: {response.StatusCode} - {responseContent}");
                    }

                    var responseObj = JsonSerializer.Deserialize<ClaudeResponse>(responseContent);
                    if (responseObj?.Content != null && responseObj.Content.Count > 0)
                    {
                        return responseObj.Content[0].Text ?? string.Empty;
                    }

                    throw new InvalidOperationException("Claude API response did not contain content.");
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == maxRetries) throw;
                    _logger.LogWarning(ex, "Claude API Transient network error (Attempt {Attempt}/{MaxRetries})", attempt, maxRetries);
                    await Task.Delay(retryDelayMs, cancellationToken);
                    retryDelayMs *= 2;
                }
            }

            return string.Empty;
        }

        private string CleanJsonSnippet(string text)
        {
            if (text.Contains("```json"))
            {
                int start = text.IndexOf("```json") + 7;
                int end = text.IndexOf("```", start);
                if (end > start)
                {
                    return text.Substring(start, end - start).Trim();
                }
            }
            else if (text.Contains("```"))
            {
                int start = text.IndexOf("```") + 3;
                int end = text.IndexOf("```", start);
                if (end > start)
                {
                    return text.Substring(start, end - start).Trim();
                }
            }
            return text.Trim();
        }

        private class AttendeeExtractionResult
        {
            public string? CandidateName { get; set; }
            public string? InterviewerName { get; set; }
        }

        private class ClaudeRequest
        {
            [System.Text.Json.Serialization.JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("messages")]
            public List<ClaudeMessage> Messages { get; set; } = new List<ClaudeMessage>();
        }

        private class ClaudeMessage
        {
            [System.Text.Json.Serialization.JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            [System.Text.Json.Serialization.JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
        }

        private class ClaudeResponse
        {
            [System.Text.Json.Serialization.JsonPropertyName("content")]
            public List<ClaudeContentBlock>? Content { get; set; }
        }

        private class ClaudeContentBlock
        {
            [System.Text.Json.Serialization.JsonPropertyName("text")]
            public string? Text { get; set; }
        }
    }
}
