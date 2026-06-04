using System;
using System.Diagnostics;

namespace LegacyWebForms.Core
{
    public class AppLogger : ILogger
    {
        public void Info(string message)
        {
            Trace.TraceInformation(message);
        }

        public void Warning(string message)
        {
            Trace.TraceWarning(message);
        }

        public void Error(string message, Exception? ex = null)
        {
            if (ex != null)
                Trace.TraceError("{0} | {1}", message, ex.ToString());
            else
                Trace.TraceError(message);
        }
    }
}
