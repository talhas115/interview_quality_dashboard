using System.Threading;
using System.Threading.Tasks;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Domain.Interfaces
{
    public interface IReportRepository
    {
        Task SaveReportAsync(AuditReport report, CancellationToken cancellationToken);
    }
}
