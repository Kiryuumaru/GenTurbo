using Application.Configuration.Extensions;
using Application.Jobs.Interfaces.Inbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application.Jobs.Workers;

internal sealed class TtlSweepWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<TtlSweepWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
                var retentionHours = configuration.RetentionHours;
                var expired = await jobService.PurgeExpiredJobsAsync(retentionHours, stoppingToken);
                if (expired.Count > 0)
                    logger.LogInformation("TTL sweep purged {Count} expired jobs", expired.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "TTL sweep error"); }
        }
    }
}
