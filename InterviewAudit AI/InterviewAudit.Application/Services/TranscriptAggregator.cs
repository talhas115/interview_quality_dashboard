using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;
using Microsoft.Extensions.Logging;

namespace InterviewAudit.Application.Services
{
    public class TranscriptAggregator : ITranscriptAggregator
    {
        private readonly ILogger<TranscriptAggregator> _logger;

        public TranscriptAggregator(ILogger<TranscriptAggregator> logger)
        {
            _logger = logger;
        }

        public MeetingTranscriptContext Aggregate(string meetingId, string meetingTitle, int expectedCount, List<Transcript> transcripts)
        {
            var context = new MeetingTranscriptContext
            {
                MeetingId = meetingId,
                MeetingTitle = meetingTitle,
                ExpectedTranscriptCount = expectedCount,
                FetchedTranscriptCount = transcripts?.Count ?? 0
            };

            if (transcripts == null || !transcripts.Any())
            {
                return context;
            }

            // Remove duplicates based on content
            var uniqueTranscripts = transcripts
                .GroupBy(t => t.Content)
                .Select(g => g.First())
                .OrderBy(t => t.CreatedDateTime ?? DateTimeOffset.MaxValue)
                .ToList();

            context.TranscriptFiles = uniqueTranscripts;

            var sb = new StringBuilder();
            
            foreach (var transcript in uniqueTranscripts)
            {
                string dateStr = transcript.CreatedDateTime.HasValue ? transcript.CreatedDateTime.Value.ToString("yyyy-MM-dd HH:mm:ss zzz") : "Unknown Date";
                
                sb.AppendLine($"===== Transcript File: {transcript.FileName} =====");
                sb.AppendLine("Created Date:");
                sb.AppendLine(dateStr);
                sb.AppendLine();
                sb.AppendLine(transcript.Content.Trim());
                sb.AppendLine();
                sb.AppendLine("===== End Transcript =====");
                sb.AppendLine();
            }

            context.CombinedTranscript = sb.ToString();

            return context;
        }
    }
}
