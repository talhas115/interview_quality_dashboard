using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Domain.Interfaces
{
    public interface ILlmService
    {
        Task<string> GenerateReportAsync(string promptTemplate, string jd, string transcript, CancellationToken cancellationToken);
        Task<(string CandidateName, string InterviewerName)> ExtractAttendeesAsync(List<Attendee> attendees, string transcriptSample, CancellationToken cancellationToken);
        Task<string> GenerateTextAsync(string prompt, int maxTokens, CancellationToken cancellationToken);
        bool IsAvailable();
    }
}
