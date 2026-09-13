using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;

namespace CoreWebForms
{
    public partial class StatCardsControl : UserControl
    {
        public void Bind(List<Product> products)
        {
            litTotalProducts.Text = products.Count.ToString();
            litLowStock.Text      = products.Count(p => p.Stock > 0 && p.Stock <= 5).ToString();
            try
            {
                litTotalOrders.Text   = AppData.Services.Orders.Count().ToString();
                litPending.Text       = AppData.Services.Orders.CountByStatus(AppConstants.OrderStatus.Pending).ToString();
            }
            catch (Exception ex) when (UiHelper.IsTransportFailure(ex))
            {
                litTotalOrders.Text = "--";
                litPending.Text = "--";
            }
        }

        public void ShowServiceUnavailable(string serviceName)
        {
            statsRow.Visible = false;
            phUnavailable.Visible = true;
            litUnavailable.Text = UiHelper.ServiceUnavailableMessage(serviceName);
        }
    }
}
