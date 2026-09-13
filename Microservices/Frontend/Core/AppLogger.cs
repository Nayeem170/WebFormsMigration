using System;
using System.Diagnostics;

namespace CoreWebForms.Core
{
    public class AppLogger : ILogger
    {
        public void Info(string message)
        {
            Trace.TraceInformation(Prefix(message));
        }

        public void Warning(string message)
        {
            Trace.TraceWarning(Prefix(message));
        }

        public void Error(string message, Exception? ex = null)
        {
            if (ex != null)
                Trace.TraceError("{0} | {1}", Prefix(message), ex.ToString());
            else
                Trace.TraceError(Prefix(message));
        }

        private static string Prefix(string message)
        {
            var id = Services.Correlation.TryCurrent();
            return id == null ? message : string.Format("[corr {0}] {1}", id, message);
        }
    }
}
