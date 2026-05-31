using System;
using System.Web.UI;

namespace LegacyWebForms
{
    public partial class ErrorPage : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            Response.StatusCode = 500;
        }
    }
}
