using System;
using System.Web.UI;

namespace LegacyWebForms
{
    public class AppPage : Page
    {
        protected override void OnInit(EventArgs e)
        {
            base.OnInit(e);
            if (Session != null && Session.SessionID != null)
                ViewStateUserKey = Session.SessionID;
        }
    }
}
