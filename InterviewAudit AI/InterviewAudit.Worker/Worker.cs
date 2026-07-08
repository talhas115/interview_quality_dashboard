using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InterviewAudit.Application.Services;
using InterviewAudit.Infrastructure.Persistence;

namespace InterviewAudit.Worker
{
    public class Worker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<Worker> _logger;
        private readonly SchedulerOptions _options;

        public Worker(
            IServiceScopeFactory scopeFactory,
            ILogger<Worker> logger,
            IOptions<SchedulerOptions> options)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Interview Audit Automation Service started. Run Interval: {Interval} seconds.", _options.IntervalSeconds);

            using (var scope = _scopeFactory.CreateScope())
            {
                var stateRepository = scope.ServiceProvider.GetRequiredService<InterviewAudit.Domain.Interfaces.IStateRepository>();
                await stateRepository.ResetFilterExecutionStateAsync(stoppingToken);

                // Run self-healing database sync if using the composite store
                if (stateRepository is CompositeStateRepository compositeRepo)
                {
                    await compositeRepo.SyncDatabaseWithJsonAsync(stoppingToken);
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting scheduled audit check...");

                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var scheduler = scope.ServiceProvider.GetRequiredService<AuditScheduler>();
                        await scheduler.ExecuteAsync(stoppingToken);
                    }
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

