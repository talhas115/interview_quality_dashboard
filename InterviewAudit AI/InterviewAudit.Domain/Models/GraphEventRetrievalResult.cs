using System;

namespace InterviewAudit.Domain.Models
{
    public class GraphEventRetrievalResult
    {
        public int TotalEventsRetrieved { get; set; }
        public int TotalPagesProcessed { get; set; }
        public TimeSpan ProcessingDuration { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
