using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Inventory.Contracts
{
    public static class AppTelemetry
    {
        public const string MeterName = "CoreWebForms";

        /// <summary>
        /// Tracing (aspnetcore + httpclient + Npgsql spans) and the app
        /// meter, wired identically in every service. Exporters are added
        /// separately by AddOtlpExporting so source-run mode (no collector)
        /// stays silent instead of retrying a dead endpoint forever.
        /// </summary>
        public static IServiceCollection AddAppTelemetry(this IServiceCollection services, string serviceName)
        {
            var otel = services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(serviceName,
                    serviceInstanceId: Environment.MachineName + ":" + Environment.ProcessId))
                .WithTracing(t => t
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql"))
                .WithMetrics(m => m
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter("Npgsql")
                    .AddMeter(MeterName));
            return services;
        }

        /// <summary>
        /// Adds OTLP trace+metric exporters only when an endpoint is
        /// configured (compose/k8s set OTEL_EXPORTER_OTLP_ENDPOINT to the
        /// collector; source-run mode leaves it unset).
        /// </summary>
        public static IServiceCollection AddOtlpExporting(this IServiceCollection services, IConfiguration configuration)
        {
            var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            if (string.IsNullOrEmpty(endpoint))
                return services;
            services.AddOpenTelemetry()
                .WithTracing(t => t.AddOtlpExporter())
                .WithMetrics(m => m.AddOtlpExporter((_, reader) =>
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15000));
            return services;
        }
    }

    /// <summary>
    /// App-owned instruments on the CoreWebForms meter. Gauges are created
    /// eagerly so AddMeter(MeterName) registers them; monitors update the
    /// backing fields.
    /// </summary>
    public static class AppMetrics
    {
        private static readonly Meter Meter = new(AppTelemetry.MeterName);

        private static int _readiness = -1;
        private static int _keyRingKeys = -1;
        private static int _keyRingActiveFingerprint = -1;

        public static Counter<long> ReserveLockTimeouts { get; } =
            Meter.CreateCounter<long>("ccw_reserve_lock_timeouts_total",
                description: "Reserve/release calls rejected with the 503 retryable lock error, by SQLSTATE class.");

        public static ObservableGauge<int> Readiness { get; } = Meter.CreateObservableGauge<int>(
            "ccw_readiness",
            () => new Measurement<int>(_readiness),
            description: "Last readiness evaluation: 1 ready, 0 not ready, -1 unknown.");

        public static ObservableGauge<int> KeyRingKeys { get; } = Meter.CreateObservableGauge<int>(
            "ccw_dataprotection_keys",
            () => new Measurement<int>(_keyRingKeys),
            description: "Number of keys visible in the shared DataProtection ring.");

        public static ObservableGauge<int> KeyRingActiveFingerprint { get; } = Meter.CreateObservableGauge<int>(
            "ccw_dataprotection_active_key_fingerprint",
            () => new Measurement<int>(_keyRingActiveFingerprint),
            description: "Stable hash of the newest ring key id; divergence across replicas is key-ring skew.");

        public static void SetReadiness(bool? ready) => _readiness = ready switch { true => 1, false => 0, _ => -1 };

        public static void SetKeyRing(int keyCount, int activeFingerprint)
        {
            _keyRingKeys = keyCount;
            _keyRingActiveFingerprint = activeFingerprint;
        }
    }
}
