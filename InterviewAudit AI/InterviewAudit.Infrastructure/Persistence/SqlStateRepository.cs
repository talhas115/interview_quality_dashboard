using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Infrastructure.Persistence
{
    public class SqlStateRepository : IStateRepository
    {
        private readonly InterviewAuditDbContext _dbContext;
        private readonly ILogger<SqlStateRepository> _logger;

        public SqlStateRepository(InterviewAuditDbContext dbContext, ILogger<SqlStateRepository> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<HashSet<string>> GetProcessedMeetingIdsAsync(string groupId, CancellationToken cancellationToken)
        {
            try
            {
                var ids = await _dbContext.ProcessedMeetings
                    .Where(m => m.GroupId == groupId)
                    .Select(m => m.MeetingId)
                    .ToListAsync(cancellationToken);

                return new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching processed meeting IDs from SQL Database for group {GroupId}", groupId);
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public async Task AddProcessedMeetingIdAsync(string groupId, ProcessedMeetingInfo meetingInfo, CancellationToken cancellationToken)
        {
            if (meetingInfo == null) return;

            try
            {
                // Prevent duplicate entries
                bool exists = await _dbContext.ProcessedMeetings
                    .AnyAsync(m => m.GroupId == groupId && m.MeetingId == meetingInfo.MeetingId, cancellationToken);

                if (!exists)
                {
                    var entity = new ProcessedMeetingEntity
                    {
                        GroupId = groupId,
                        MeetingId = meetingInfo.MeetingId,
                        CandidateId = meetingInfo.CandidateId,
                        CandidateName = meetingInfo.CandidateName,
                        InterviewerName = meetingInfo.InterviewerName,
                        StartDateTime = meetingInfo.StartDateTime,
                        EndDateTime = meetingInfo.EndDateTime,
                        CandidateScore = meetingInfo.CandidateScore,
                        InterviewerScore = meetingInfo.InterviewerScore,
                        JdAlignmentScore = meetingInfo.JdAlignmentScore,
                        ProcessedAt = DateTimeOffset.UtcNow
                    };

                    await _dbContext.ProcessedMeetings.AddAsync(entity, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Saved meeting {MeetingId} metadata to SQL Database", meetingInfo.MeetingId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save processed meeting {MeetingId} to SQL Database", meetingInfo.MeetingId);
                throw;
            }
        }

        public async Task<bool> IsFilterExecutionCompletedAsync(CancellationToken cancellationToken)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "filter-completed.json");
            return await Task.FromResult(File.Exists(path));
        }

        public async Task ResetFilterExecutionStateAsync(CancellationToken cancellationToken)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "filter-completed.json");
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reset filter completed file.");
                }
            }
            await Task.CompletedTask;
        }

        public async Task SetFilterExecutionCompletedAsync(CancellationToken cancellationToken)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "filter-completed.json");
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await File.WriteAllTextAsync(path, "{\"completed\": true}", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write filter completed file.");
            }
        }
    }
}
