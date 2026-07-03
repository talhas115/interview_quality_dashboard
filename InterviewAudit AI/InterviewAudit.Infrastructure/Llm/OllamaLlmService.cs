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
    public class OllamaLlmService : ILlmService
    {
        public bool IsAvailable() => true;
        private readonly string _baseUrl;
        private readonly string _modelName;
        private readonly ILogger<OllamaLlmService> _logger;
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

        public OllamaLlmService(string baseUrl, string modelName, ILogger<OllamaLlmService> logger)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl.TrimEnd('/');
            _modelName = modelName;
            _logger = logger;
        }

        public async Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Ollama: Initiating quality audit report generation using model {Model}...", _modelName);

            string fullPrompt = promptTemplate
                .Replace("{{JD}}", jd)
                .Replace("{{TRANSCRIPT}}", transcript)
                .Replace("<PASTE JD HERE>", jd)
                .Replace("<PASTE INTERVIEW TRANSCRIPT HERE>", transcript);

            return await CallOllamaApiAsync(fullPrompt, cancellationToken);
        }

        public async Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            return await CallOllamaApiAsync(prompt, cancellationToken);
        }

        public async Task<(string CandidateName, string InterviewerName)> ExtractAttendeesAsync(List<Attendee> attendees, string transcriptSample, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Ollama: Extracting candidate and interviewer names using model {Model}...", _modelName);

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

            string responseText = await CallOllamaApiAsync(extractionPrompt, cancellationToken);
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
                _logger.LogWarning(ex, "Ollama: Failed to parse attendee extraction JSON. Response was: {Response}", responseText);
            }

            return (string.Empty, string.Empty);
        }

        private async Task<string> CallOllamaApiAsync(string prompt, CancellationToken cancellationToken)
        {
            string url = $"{_baseUrl}/api/chat";

            var payload = new
            {
                model = _modelName,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                stream = false
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            _logger.LogInformation("Ollama Request -> Model: {Model}, BaseUrl: {Url}", _modelName, _baseUrl);

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                var response = await HttpClient.SendAsync(request, cancellationToken);
                string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Ollama API Error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                    throw new InvalidOperationException($"Ollama API Error: {response.StatusCode} - {responseContent}");
                }

                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("message", out var messageElement) && 
                    messageElement.TryGetProperty("content", out var contentElement))
                {
                    return contentElement.GetString() ?? string.Empty;
                }

                _logger.LogWarning("Ollama API response did not contain the expected 'message.content' format.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ollama API: Request failed.");
                throw;
            }
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
