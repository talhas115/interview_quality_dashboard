using System.Collections.Generic;

namespace InterviewAudit.Domain.Models
{
    public class MeetingTranscriptContext
    {
        public string MeetingId { get; set; } = string.Empty;
        public string MeetingTitle { get; set; } = string.Empty;
        public int ExpectedTranscriptCount { get; set; }
        public int FetchedTranscriptCount { get; set; }
        public List<Transcript> TranscriptFiles { get; set; } = new List<Transcript>();
        public string CombinedTranscript { get; set; } = string.Empty;
    }
}
