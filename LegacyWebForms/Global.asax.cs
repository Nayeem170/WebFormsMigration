using System;
using System.Web;

namespace LegacyWebForms
{
    public class Global : HttpApplication
    {
        void Application_Start(object sender, EventArgs e)
        {
            string appDataPath = Server.MapPath("~/App_Data");
            AppData.Initialize(appDataPath);
        }

        void Session_Start(object sender, EventArgs e)
        {
        }

        void Application_Error(object sender, EventArgs e)
        {
            var ex = Server.GetLastError();
            if (ex != null)
                System.Diagnostics.Trace.TraceError("Unhandled exception: {0}", ex);
        }
    }
}
