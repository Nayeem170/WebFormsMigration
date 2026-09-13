using System;
using System.Web.UI;

namespace CoreWebForms
{
    public partial class OrdersPage : AppPage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                orderWizard.Bind();
                orderHistory.Bind();
                ordersManage.Bind();
            }
        }

        protected void orderWizard_OrderPlaced(object sender, OrderEventArgs e)
        {
            orderWizard.Bind();
            orderHistory.Bind();
            ordersManage.Bind();
        }

        protected void ordersManage_OrderDeleted(object sender, EventArgs e)
        {
            orderHistory.Bind();
        }
    }
}
