using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InterviewAudit.Domain.Interfaces;
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
                        if (item.Length > 0 && !string.IsNullOrEmpty(item[0]))
                        {
                            ids.Add(item[0]);
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

        public async Task AddProcessedMeetingIdAsync(string groupId, string meetingId, string candidateId, CancellationToken cancellationToken)
        {
            await FileLock.WaitAsync(cancellationToken);
            try
            {
                var state = await LoadStateAsync(cancellationToken);
                if (!state.TryGetValue(groupId, out var list))
                {
                    list = new List<string[]>();
                    state[groupId] = list;
                }

                bool exists = false;
                foreach(var item in list)
                {
                    if (item.Length > 0 && item[0].Equals(meetingId, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    list.Add(new string[] { meetingId, candidateId ?? "" });
                    await SaveStateAsync(state, cancellationToken);
                    _logger.LogInformation("Added meeting {MeetingId} with Candidate {CandidateId} to processed list for group {GroupId}", meetingId, candidateId, groupId);
                }
            }
            finally
            {
                FileLock.Release();
            }
        }

        private async Task<Dictionary<string, List<string[]>>> LoadStateAsync(CancellationToken cancellationToken)
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                string json = await File.ReadAllTextAsync(_filePath, cancellationToken);
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
                }

                try 
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, List<string[]>>>(json);
                    return dict != null 
                        ? new Dictionary<string, List<string[]>>(dict, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                    // Fallback for old dictionary format
                    var oldDict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                    var newDict = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
                    if (oldDict != null)
                    {
                        foreach (var kvp in oldDict)
                        {
                            var newList = new List<string[]>();
                            foreach(var id in kvp.Value)
                            {
                                newList.Add(new string[] { id, "" });
                            }
                            newDict[kvp.Key] = newList;
                        }
                    }
                    return newDict;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load state file from {Path}. Starting with empty state.", _filePath);
                return new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private async Task SaveStateAsync(Dictionary<string, List<string[]>> state, CancellationToken cancellationToken)
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


