using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Infrastructure.Llm
{
    public class OpenAiLlmService : ILlmService
    {
        public bool IsAvailable() => _apiKeyManager.HasAvailableKeys("OpenAI");
        private readonly ILlmApiKeyManager _apiKeyManager;
        private readonly string _modelName;
        private readonly ILogger<OpenAiLlmService> _logger;

        public OpenAiLlmService(ILlmApiKeyManager apiKeyManager, string modelName, ILogger<OpenAiLlmService> logger)
        {
            _apiKeyManager = apiKeyManager;
            _modelName = modelName;
            _logger = logger;
        }

        public async Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken)
        {
            _logger.LogInformation("OpenAI: Initiating quality audit report generation using model {Model}...", _modelName);

            string fullPrompt = promptTemplate
                .Replace("{{JD}}", jd)
                .Replace("{{TRANSCRIPT}}", transcript)
                .Replace("<PASTE JD HERE>", jd)
                .Replace("<PASTE INTERVIEW TRANSCRIPT HERE>", transcript);

            return await CallOpenAiChatAsync(fullPrompt, cancellationToken);
        }

        public async Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            return await CallOpenAiChatAsync(prompt, cancellationToken);
        }

        public async Task<(string CandidateName, string InterviewerName)> ExtractAttendeesAsync(List<Attendee> attendees, string transcriptSample, CancellationToken cancellationToken)
        {
            _logger.LogInformation("OpenAI: Extracting candidate and interviewer names using model {Model}...", _modelName);

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
                string responseText = await CallOpenAiChatAsync(extractionPrompt, cancellationToken);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<AttendeeExtractionResult>(CleanJsonSnippet(responseText), options);

                if (result != null)
                {
                    _logger.LogInformation("OpenAI: Extracted Candidate: {Candidate}, Interviewer: {Interviewer}", result.CandidateName, result.InterviewerName);
                    return (result.CandidateName ?? string.Empty, result.InterviewerName ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI: Failed to extract attendees using model {Model}.", _modelName);
            }

            return (string.Empty, string.Empty);
        }

        private async Task<string> CallOpenAiChatAsync(string prompt, CancellationToken cancellationToken)
        {
            int maxRetries = 5;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                string activeKey = _apiKeyManager.GetNextAvailableKey("OpenAI");
                if (string.IsNullOrWhiteSpace(activeKey))
                {
                    _logger.LogError("OpenAI API: No available API keys. All keys are currently exhausted.");
                    throw new InvalidOperationException("All OpenAI API keys are exhausted. Cannot proceed.");
                }

                try
                {
                    var client = new OpenAIClient(activeKey);
                    var chatClient = client.GetChatClient(_modelName);

                    var chatMessages = new List<ChatMessage>
                    {
                        new UserChatMessage(prompt)
                    };

                    var completion = await chatClient.CompleteChatAsync(chatMessages, cancellationToken: cancellationToken);
                    return completion.Value.Content[0].Text ?? string.Empty;
                }
                catch (System.ClientModel.ClientResultException ex) when (ex.Status == 429)
                {
                    _logger.LogWarning("OpenAI API Rate limit exhausted (429) for current key. Attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                    _apiKeyManager.MarkKeyExhausted("OpenAI", activeKey);
                    if (attempt == maxRetries)
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OpenAI API call failed on attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                    if (attempt == maxRetries)
                    {
                        throw;
                    }
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }
            throw new InvalidOperationException("OpenAI API call failed: max retries reached.");
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
    }
}
