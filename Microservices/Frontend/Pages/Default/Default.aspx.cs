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
            BindCatalogZones();
            BindOrdersZone();
        }

        private void BindCatalogZones()
        {
            List<Product> products;
            try
            {
                products = AppData.Services.Products.GetAll();
            }
            catch (Exception ex) when (UiHelper.IsTransportFailure(ex))
            {
                statCards.ShowServiceUnavailable("Catalog");
                catExpand.ShowServiceUnavailable("Catalog");
                outOfStock.Visible = false;
                return;
            }
            statCards.Bind(products);
            catExpand.Bind(products);
            outOfStock.Bind(products);
        }

        private void BindOrdersZone()
        {
            try
            {
                ordersTable.Bind(AppData.Services.Orders.GetRecent(6));
            }
            catch (Exception ex) when (UiHelper.IsTransportFailure(ex))
            {
                ordersTable.ShowServiceUnavailable("Orders");
            }
        }
    }
}
