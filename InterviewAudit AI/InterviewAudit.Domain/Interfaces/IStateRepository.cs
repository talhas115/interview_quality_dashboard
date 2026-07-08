using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Domain.Interfaces
{
    public interface IStateRepository
    {
        Task<HashSet<string>> GetProcessedMeetingIdsAsync(string groupId, CancellationToken cancellationToken);
        Task AddProcessedMeetingIdAsync(string groupId, ProcessedMeetingInfo meetingInfo, CancellationToken cancellationToken);
        Task<bool> IsFilterExecutionCompletedAsync(CancellationToken cancellationToken);
        Task SetFilterExecutionCompletedAsync(CancellationToken cancellationToken);
        Task ResetFilterExecutionStateAsync(CancellationToken cancellationToken);
    }
}

