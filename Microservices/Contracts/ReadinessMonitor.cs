using Inventory.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inventory.Contracts
{
    /// <summary>
    /// Evaluates the same readiness rule the /health/ready endpoint serves,
    /// on a timer, recording it on the ccw_readiness gauge and logging
    /// transitions. "Know before users do" needs the signal observable
    /// without a request landing on the pod.
    /// </summary>
    public sealed class ReadinessMonitor(
        IServiceProvider provider,
        ILogger<ReadinessMonitor> logger,
        Func<IServiceProvider, CancellationToken, Task<bool>> check,
        TimeSpan? interval = null) : BackgroundService
    {
        private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(10);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            bool? last = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                var current = false;
                try
                {
                    current = await check(provider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    current = false;
                }
                if (last != current)
                {
                    logger.LogInformation("readiness: {State} (monitor transition)", current);
                    last = current;
                }
                AppMetrics.SetReadiness(current);
                try { await Task.Delay(_interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
