using System;

namespace InterviewAudit.Domain.Models
{
    public class Transcript
    {
        public string Id { get; set; } = string.Empty;
        public string MeetingId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTimeOffset? CreatedDateTime { get; set; }
    }
}
