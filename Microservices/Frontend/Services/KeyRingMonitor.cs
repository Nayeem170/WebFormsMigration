using Inventory.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CoreWebForms.Services
{
    /// <summary>
    /// Key-ring skew made observable: reads the shared DataProtection ring
    /// from Redis on a timer, records key count and active-key identity on
    /// the CoreWebForms meter, and logs both per replica. Skew (a replica
    /// validating only its own fresh key) is visible here before any
    /// browser postback finds it; smoke-parity's cross-replica resource
    /// rounds stay as the regression gate.
    /// </summary>
    internal sealed class KeyRingMonitor(
        IConnectionMultiplexer multiplexer,
        ILogger<KeyRingMonitor> logger) : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Ground truth from the live ring: the StackExchangeRedis
                    // XML repository stores entries as elements of a single
                    // redis LIST named by the key ring name (not a hash, not
                    // per-entry string keys).
                    var values = await multiplexer.GetDatabase().ListRangeAsync("DataProtection-Keys");
                    var summary = KeyRingParser.Parse(values.Where(v => !v.IsNullOrEmpty).Select(v => (string)v!));
                    AppMetrics.SetKeyRing(summary.KeyCount, summary.ActiveFingerprint);
                    logger.LogInformation(
                        "keyring: keys={Keys} active={ActiveKey} fingerprint={Fingerprint} malformed={Malformed}",
                        summary.KeyCount, summary.ActiveKeyId ?? "none", summary.ActiveFingerprint, summary.Malformed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppMetrics.SetKeyRing(-1, -1);
                    logger.LogWarning(ex, "keyring: read failed");
                }
                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
