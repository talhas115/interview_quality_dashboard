using System.Threading;
using System.Threading.Tasks;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Domain.Interfaces
{
    public interface IChunkedLlmProcessor
    {
        Task<string> ProcessAsync(MeetingTranscriptContext context, string promptTemplate, string jd, CancellationToken cancellationToken);
    }
}
