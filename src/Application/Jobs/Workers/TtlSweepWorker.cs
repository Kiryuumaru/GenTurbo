using Application.Configuration.Extensions;
using Application.Jobs.Interfaces.Inbound;
using Application.Workers.Interfaces.Outbound;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Application.Jobs.Workers;

internal sealed class TtlSweepWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TtlSweepWorker> logger) : BackgroundService
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
                var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

                var retentionHours = configuration.RetentionHours;
                var expiredJobs = await jobService.PurgeExpiredJobsAsync(retentionHours, stoppingToken);

                foreach (var job in expiredJobs)
                {
                    if (job.OutputUrl is not null)
                        fileStorage.Delete(job.OutputUrl);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TTL sweep error");
            }
        }
    }
}
