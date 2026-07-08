using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;
using InterviewAudit.Application.Services;

namespace InterviewAudit.Infrastructure.Persistence
{
    public class FileSystemStateRepository : IStateRepository
    {
        private readonly string _filePath;
        private readonly ILogger<FileSystemStateRepository> _logger;
        private static readonly SemaphoreSlim FileLock = new SemaphoreSlim(1, 1);

        public FileSystemStateRepository(IOptions<StorageOptions> storageOptions, ILogger<FileSystemStateRepository> logger)
        {
            _filePath = storageOptions.Value.StateFilePath;
            _logger = logger;
        }

        public async Task<HashSet<string>> GetProcessedMeetingIdsAsync(string groupId, CancellationToken cancellationToken)
        {
            await FileLock.WaitAsync(cancellationToken);
            try
            {
                var state = await LoadStateAsync(cancellationToken);
                if (state.TryGetValue(groupId, out var list))
                {
                    var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var item in list)
                    {
                        if (item != null && !string.IsNullOrEmpty(item.MeetingId))
                        {
                            ids.Add(item.MeetingId);
                        }
                    }
                    return ids;
                }
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task<bool> IsFilterExecutionCompletedAsync(CancellationToken cancellationToken)
        {
            string filterStatePath = Path.Combine(Path.GetDirectoryName(_filePath) ?? "", "filter-completed.json");
            return await Task.FromResult(File.Exists(filterStatePath));
        }

        public async Task ResetFilterExecutionStateAsync(CancellationToken cancellationToken)
        {
            string filterStatePath = Path.Combine(Path.GetDirectoryName(_filePath) ?? "", "filter-completed.json");
            if (File.Exists(filterStatePath))
            {
                try
                {
                    File.Delete(filterStatePath);
                    _logger.LogInformation("Filter execution completed state reset.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete filter state file at {Path}.", filterStatePath);
                }
            }
            await Task.CompletedTask;
        }

        public async Task SetFilterExecutionCompletedAsync(CancellationToken cancellationToken)
        {
            string filterStatePath = Path.Combine(Path.GetDirectoryName(_filePath) ?? "", "filter-completed.json");
            try
            {
                string? directory = Path.GetDirectoryName(filterStatePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await File.WriteAllTextAsync(filterStatePath, "{\"completed\": true}", cancellationToken);
                _logger.LogInformation("Filter execution completed state saved.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save filter state file to {Path}.", filterStatePath);
            }
        }

        public async Task AddProcessedMeetingIdAsync(string groupId, ProcessedMeetingInfo meetingInfo, CancellationToken cancellationToken)
        {
            if (meetingInfo == null) return;

            await FileLock.WaitAsync(cancellationToken);
            try
            {
                var state = await LoadStateAsync(cancellationToken);
                if (!state.TryGetValue(groupId, out var list))
                {
                    list = new List<ProcessedMeetingInfo>();
                    state[groupId] = list;
                }

                bool exists = false;
                foreach(var item in list)
                {
                    if (item != null && item.MeetingId.Equals(meetingInfo.MeetingId, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    list.Add(meetingInfo);
                    await SaveStateAsync(state, cancellationToken);
                    _logger.LogInformation("Added meeting {MeetingId} with Candidate {CandidateId} and metadata to processed list for group {GroupId}", meetingInfo.MeetingId, meetingInfo.CandidateId, groupId);
                }
            }
            finally
            {
                FileLock.Release();
            }
        }

        public async Task<Dictionary<string, List<ProcessedMeetingInfo>>> GetAllProcessedMeetingsAsync(CancellationToken cancellationToken)
        {
            await FileLock.WaitAsync(cancellationToken);
            try
            {
                return await LoadStateAsync(cancellationToken);
            }
            finally
            {
                FileLock.Release();
            }
        }

        private async Task<Dictionary<string, List<ProcessedMeetingInfo>>> LoadStateAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, List<ProcessedMeetingInfo>>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                string json = await File.ReadAllTextAsync(_filePath, cancellationToken);
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new Dictionary<string, List<ProcessedMeetingInfo>>(StringComparer.OrdinalIgnoreCase);
                }

                // 1. Try to deserialize the new metadata structure
                try 
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, List<ProcessedMeetingInfo>>>(json);
                    if (dict != null)
                    {
                        return new Dictionary<string, List<ProcessedMeetingInfo>>(dict, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // Fallback to older formats
                }

                // 2. Fallback to intermediate string[] structure
                try
                {
                    var midDict = JsonSerializer.Deserialize<Dictionary<string, List<string[]>>>(json);
                    var newDict = new Dictionary<string, List<ProcessedMeetingInfo>>(StringComparer.OrdinalIgnoreCase);
                    if (midDict != null)
                    {
                        foreach (var kvp in midDict)
                        {
                            var newList = new List<ProcessedMeetingInfo>();
                            foreach (var item in kvp.Value)
                            {
                                if (item != null && item.Length > 0)
                                {
                                    newList.Add(new ProcessedMeetingInfo
                                    {
                                        MeetingId = item[0],
                                        CandidateId = item.Length > 1 ? item[1] : ""
                                    });
                                }
                            }
                            newDict[kvp.Key] = newList;
                        }
                        return newDict;
                    }
                }
                catch
                {
                    // Fallback to basic string list
                }

                // 3. Fallback for oldest basic string list format
                try
                {
                    var oldDict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                    var newDict = new Dictionary<string, List<ProcessedMeetingInfo>>(StringComparer.OrdinalIgnoreCase);
                    if (oldDict != null)
                    {
                        foreach (var kvp in oldDict)
                        {
                            var newList = new List<ProcessedMeetingInfo>();
                            foreach (var id in kvp.Value)
                            {
                                newList.Add(new ProcessedMeetingInfo
                                {
                                    MeetingId = id,
                                    CandidateId = ""
                                });
                            }
                            newDict[kvp.Key] = newList;
                        }
                        return newDict;
                    }
                }
                catch
                {
                    // Catch and let it fall back
                }

                return new Dictionary<string, List<ProcessedMeetingInfo>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load state file from {Path}. Starting with empty state.", _filePath);
                return new Dictionary<string, List<ProcessedMeetingInfo>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task SaveStateAsync(Dictionary<string, List<ProcessedMeetingInfo>> state, CancellationToken cancellationToken)
        {
            try
            {
                string? directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(state, options);
                await File.WriteAllTextAsync(_filePath, json, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save state file to {Path}.", _filePath);
                throw;
            }
        }
    }
}


