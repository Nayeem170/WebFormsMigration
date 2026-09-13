using System;
using System.Collections.Generic;
using System.Web.UI;

namespace CoreWebForms
{
    public partial class DefaultPage : AppPage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
                BindDashboard();
        }

        private void BindDashboard()
        {
            var products = AppData.Services.Products.GetAll();
            statCards.Bind(products);
            ordersTable.Bind(AppData.Services.Orders.GetRecent(6));
            catExpand.Bind(products);
            outOfStock.Bind(products);
        }
    }
}
