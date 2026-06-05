using System;
using System.Web.UI;

namespace LegacyWebForms
{
    public partial class SiteMaster : MasterPage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            var p = Request.AppRelativeCurrentExecutionFilePath.ToLower();
            lnkDash.Attributes["class"]     = "nav-link" + (p.Contains("/default/") || p == "~/default.aspx" || p == "~/" ? " active" : "");
            lnkProducts.Attributes["class"] = "nav-link" + (p.Contains("/products/") || p == "~/products.aspx" ? " active" : "");
            lnkOrders.Attributes["class"]   = "nav-link" + (p.Contains("/orders/") || p == "~/orders.aspx" ? " active" : "");
        }
    }
}
