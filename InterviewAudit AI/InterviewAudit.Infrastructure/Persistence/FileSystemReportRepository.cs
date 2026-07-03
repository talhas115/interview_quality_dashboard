using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InterviewAudit.Domain.Interfaces;
using InterviewAudit.Domain.Models;
using InterviewAudit.Application.Services;

namespace InterviewAudit.Infrastructure.Persistence
{
    public class FileSystemReportRepository : IReportRepository
    {
        private readonly string _reportsFolder;
        private readonly ILogger<FileSystemReportRepository> _logger;

        public FileSystemReportRepository(IOptions<StorageOptions> storageOptions, ILogger<FileSystemReportRepository> logger)
        {
            _reportsFolder = storageOptions.Value.ReportsFolder;
            _logger = logger;
        }

        public async Task SaveReportAsync(AuditReport report, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(_reportsFolder))
                {
                    _logger.LogInformation("Creating reports directory at {Path}", _reportsFolder);
                    Directory.CreateDirectory(_reportsFolder);
                }

                string filePath = Path.Combine(_reportsFolder, report.FileName);
                _logger.LogInformation("Writing audit report to {Path}", filePath);
                await File.WriteAllTextAsync(filePath, report.Content, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save audit report {FileName} to {Folder}", report.FileName, _reportsFolder);
                throw;
            }
        }
    }
}
