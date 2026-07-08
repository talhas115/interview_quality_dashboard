using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;

namespace InterviewAudit.Infrastructure.Persistence
{
    public class CompositeStateRepository : IStateRepository
    {
        private readonly FileSystemStateRepository _fileSystemRepo;
        private readonly SqlStateRepository _sqlRepo;
        private readonly ILogger<CompositeStateRepository> _logger;

        public CompositeStateRepository(
            FileSystemStateRepository fileSystemRepo,
            SqlStateRepository sqlRepo,
            ILogger<CompositeStateRepository> logger)
        {
            _fileSystemRepo = fileSystemRepo;
            _sqlRepo = sqlRepo;
            _logger = logger;
        }

        public async Task<HashSet<string>> GetProcessedMeetingIdsAsync(string groupId, CancellationToken cancellationToken)
        {
            // Read strictly from processed-meetings.json to ensure it is the authoritative duplicate checker
            return await _fileSystemRepo.GetProcessedMeetingIdsAsync(groupId, cancellationToken);
        }

        public async Task AddProcessedMeetingIdAsync(string groupId, ProcessedMeetingInfo meetingInfo, CancellationToken cancellationToken)
        {
            // 1. Write to processed-meetings.json first (must succeed)
            await _fileSystemRepo.AddProcessedMeetingIdAsync(groupId, meetingInfo, cancellationToken);

            // 2. Write to SQL Database after JSON write succeeds
            try
            {
                await _sqlRepo.AddProcessedMeetingIdAsync(groupId, meetingInfo, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log DB failure but do not throw, since JSON is successfully updated
                _logger.LogError(ex, "Failed to write meeting {MeetingId} metadata to SQL Database, but JSON file write succeeded.", meetingInfo.MeetingId);
            }
        }

        public async Task SyncDatabaseWithJsonAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting database synchronization check with processed-meetings.json...");

                // 1. Load all processed meetings from JSON
                var jsonState = await _fileSystemRepo.GetAllProcessedMeetingsAsync(cancellationToken);

                // 2. Compare and sync missing records to the SQL Database
                int synchronizedCount = 0;
                foreach (var groupKvp in jsonState)
                {
                    string groupId = groupKvp.Key;
                    var dbIds = await _sqlRepo.GetProcessedMeetingIdsAsync(groupId, cancellationToken);

                    foreach (var meeting in groupKvp.Value)
                    {
                        if (meeting == null || string.IsNullOrEmpty(meeting.MeetingId)) continue;

                        if (!dbIds.Contains(meeting.MeetingId))
                        {
                            _logger.LogInformation("Syncing missing meeting {MeetingId} from JSON to SQL Database...", meeting.MeetingId);
                            await _sqlRepo.AddProcessedMeetingIdAsync(groupId, meeting, cancellationToken);
                            synchronizedCount++;
                        }
                    }
                }

                if (synchronizedCount > 0)
                {
                    _logger.LogInformation("Successfully synchronized {Count} missing meeting(s) to SQL Database.", synchronizedCount);
                }
                else
                {
                    _logger.LogInformation("Database is fully in sync with processed-meetings.json.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete database synchronization with JSON file.");
            }
        }

        public async Task<bool> IsFilterExecutionCompletedAsync(CancellationToken cancellationToken)
        {
            return await _fileSystemRepo.IsFilterExecutionCompletedAsync(cancellationToken);
        }

        public async Task SetFilterExecutionCompletedAsync(CancellationToken cancellationToken)
        {
            await _fileSystemRepo.SetFilterExecutionCompletedAsync(cancellationToken);
        }

        public async Task ResetFilterExecutionStateAsync(CancellationToken cancellationToken)
        {
            await _fileSystemRepo.ResetFilterExecutionStateAsync(cancellationToken);
        }
    }
}
