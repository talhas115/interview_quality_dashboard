namespace InterviewAudit.Application.Services
{
    public class InterviewFilterSettings
    {
        public bool EnableFilterMode { get; set; } = false;
        public string InterviewName { get; set; } = string.Empty;
        public int RecentDays { get; set; } = 2;
        public bool StopSchedulerAfterCompletion { get; set; } = false;
    }
}
