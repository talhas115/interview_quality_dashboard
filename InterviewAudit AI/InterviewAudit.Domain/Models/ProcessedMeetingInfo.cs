using System;

namespace InterviewAudit.Domain.Models
{
    public class ProcessedMeetingInfo
    {
        public string MeetingId { get; set; } = string.Empty;
        public string CandidateId { get; set; } = string.Empty;
        public string CandidateName { get; set; } = string.Empty;
        public string InterviewerName { get; set; } = string.Empty;
        public string StartDateTime { get; set; } = string.Empty;
        public string EndDateTime { get; set; } = string.Empty;
        public int? CandidateScore { get; set; }
        public int? InterviewerScore { get; set; }
        public int? JdAlignmentScore { get; set; }
    }
}
