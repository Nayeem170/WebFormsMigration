using System;
using System.Web;

namespace CoreWebForms
{
    public class InventoryApp : HttpApplication
    {
        void Application_EndRequest(object sender, EventArgs e)
        {
            if (Request == null || Response == null) return;
            AppData.Services?.Log.Info(string.Format("HTTP {0} {1} -> {2}",
                Request.HttpMethod, Request.RawUrl, Response.StatusCode));
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
