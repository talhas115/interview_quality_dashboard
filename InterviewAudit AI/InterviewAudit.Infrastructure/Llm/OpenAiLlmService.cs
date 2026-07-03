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
        public bool IsAvailable() => true;
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly ILogger<OpenAiLlmService> _logger;

        public OpenAiLlmService(string apiKey, string modelName, ILogger<OpenAiLlmService> logger)
        {
            _apiKey = apiKey;
            _modelName = modelName;
            _logger = logger;
        }

        public async Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken)
        {
            _logger.LogInformation("OpenAI: Initiating quality audit report generation using model {Model}...", _modelName);

            // Replace variables in Master Prompt
            string fullPrompt = promptTemplate
                .Replace("{{JD}}", jd)
                .Replace("{{TRANSCRIPT}}", transcript)
                // If there's a legacy marker <PASTE JD HERE> or <PASTE INTERVIEW TRANSCRIPT HERE>
                .Replace("<PASTE JD HERE>", jd)
                .Replace("<PASTE INTERVIEW TRANSCRIPT HERE>", transcript);

            try
            {
                var client = new OpenAIClient(_apiKey);
                var chatClient = client.GetChatClient(_modelName);

                var chatMessages = new List<ChatMessage>
                {
                    new UserChatMessage(fullPrompt)
                };

                var completion = await chatClient.CompleteChatAsync(chatMessages, cancellationToken: cancellationToken);
                string responseText = completion.Value.Content[0].Text;

                return responseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenAI: Failed to generate report using model {Model}.", _modelName);
                throw;
            }
        }

        public async Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
        {
            return await Task.FromResult(string.Empty);
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
                var client = new OpenAIClient(_apiKey);
                var chatClient = client.GetChatClient(_modelName);

                var chatMessages = new List<ChatMessage>
                {
                    new UserChatMessage(extractionPrompt)
                };

                var completion = await chatClient.CompleteChatAsync(chatMessages, cancellationToken: cancellationToken);
                string responseText = completion.Value.Content[0].Text;

                // Simple JSON parser
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

        private string CleanJsonSnippet(string text)
        {
            // Strip markdown block quotes if present
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








