using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Domain.Interfaces
{
    public interface IGraphService
    {
        Task<GraphEventRetrievalResult> ProcessOrganizerMeetingsAsync(string userId, Func<List<Meeting>, Task<bool>> pageProcessor, CancellationToken cancellationToken);
        Task<List<Transcript>> GetTranscriptsAsync(string userId, string meetingId, string joinUrl, CancellationToken cancellationToken);
    }
}

