using System;
using System.Web.UI;

namespace LegacyWebForms
{
    public partial class SiteMaster : MasterPage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            var p = Request.AppRelativeCurrentExecutionFilePath.ToLower();
            lnkDash.Attributes["class"]     = "nav-link" + (p == "~/default.aspx" || p == "~/" ? " active" : "");
            lnkProducts.Attributes["class"] = "nav-link" + (p.Contains("products") ? " active" : "");
            lnkOrders.Attributes["class"]   = "nav-link" + (p.Contains("orders") ? " active" : "");
        }
    }
}
