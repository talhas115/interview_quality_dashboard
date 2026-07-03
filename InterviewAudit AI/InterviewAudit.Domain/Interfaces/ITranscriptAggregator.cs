using System.Collections.Generic;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Domain.Interfaces
{
    public interface ITranscriptAggregator
    {
        MeetingTranscriptContext Aggregate(string meetingId, string meetingTitle, int expectedCount, List<Transcript> transcripts);
    }
}
