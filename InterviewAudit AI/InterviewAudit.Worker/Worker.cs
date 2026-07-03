using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InterviewAudit.Application.Services;

namespace InterviewAudit.Worker
{
    public class Worker : BackgroundService
    {
        private readonly AuditScheduler _scheduler;
        private readonly ILogger<Worker> _logger;
        private readonly SchedulerOptions _options;

        private readonly InterviewAudit.Domain.Interfaces.IStateRepository _stateRepository;

        public Worker(
            AuditScheduler scheduler,
            ILogger<Worker> logger,
            IOptions<SchedulerOptions> options,
            InterviewAudit.Domain.Interfaces.IStateRepository stateRepository)
        {
            _scheduler = scheduler;
            _logger = logger;
            _options = options.Value;
            _stateRepository = stateRepository;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Interview Audit Automation Service started. Run Interval: {Interval} seconds.", _options.IntervalSeconds);
            await _stateRepository.ResetFilterExecutionStateAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting scheduled audit check...");
                    await _scheduler.ExecuteAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during the scheduled audit execution loop.");
                }

                _logger.LogInformation("Waiting for {Interval} seconds before next check...", _options.IntervalSeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Graceful shutdown requested
                    break;
                }
            }

            _logger.LogInformation("Interview Audit Automation Service is shutting down.");
        }
    }
}

