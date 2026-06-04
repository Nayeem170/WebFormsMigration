using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Web;

namespace LegacyWebForms
{
    public class InventoryApp : HttpApplication
    {
        void Application_Start(object sender, EventArgs e)
        {
            var rawDbPath = ConfigurationManager.AppSettings["DbPath"]
                ?? throw new InvalidOperationException("AppSettings key \"DbPath\" is missing from web.config.");
            string dbPath = HttpRuntime.AppDomainAppPath + rawDbPath.TrimStart('~', '/');
            AppData.Initialize(dbPath);

            var logPath = Path.Combine(HttpRuntime.AppDomainAppPath, "App_Data", "logs", "app.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            if (Trace.Listeners["file"] == null)
            {
                var listener = new TextWriterTraceListener(logPath, "file")
                {
                    TraceOutputOptions = TraceOptions.DateTime
                };
                Trace.Listeners.Add(listener);
            }
            Trace.AutoFlush = true;
        }

        void Application_Error(object sender, EventArgs e)
        {
            var ex = Server.GetLastError();
            if (ex == null) return;

            var baseEx = ex.GetBaseException();
            AppData.Services?.Log.Error(string.Format("Unhandled error on {0}", Request?.RawUrl), baseEx);
        }
    }
}
