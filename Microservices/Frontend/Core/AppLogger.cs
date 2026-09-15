using System;
using Microsoft.Extensions.Logging;

namespace CoreWebForms.Core
{
    /// <summary>
    /// Legacy app-facing logger: interface kept (pages and services call
    /// it), implementation now delegates to Microsoft.Extensions.Logging
    /// JSON stdout instead of System.Diagnostics.Trace. The Trace route
    /// was invisible to docker/kubectl logging and its file listener had
    /// no rotation and local-time timestamps.
    /// </summary>
    public class AppLogger : ILogger
    {
        private static volatile Microsoft.Extensions.Logging.ILogger _sink =
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        internal static void Use(ILoggerFactory factory)
        {
            _sink = factory.CreateLogger("CoreWebForms.App");
        }

        public void Info(string message)
        {
            _sink.LogInformation("{AppMessage}", Prefix(message));
        }

        public void Warning(string message)
        {
            _sink.LogWarning("{AppMessage}", Prefix(message));
        }

        public void Error(string message, Exception? ex = null)
        {
            if (ex != null)
                _sink.LogError(ex, "{AppMessage}", Prefix(message));
            else
                _sink.LogError("{AppMessage}", Prefix(message));
        }

        private static string Prefix(string message)
        {
            var id = Services.Correlation.TryCurrent();
            return id == null ? message : string.Format("[corr {0}] {1}", id, message);
        }
    }
}
