namespace InterviewAudit.Domain.Models
{
    public class AuditReport
    {
        public string FileName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public string MeetingId { get; set; } = string.Empty;
    }
}
