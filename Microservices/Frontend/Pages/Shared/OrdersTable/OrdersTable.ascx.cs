using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;

namespace CoreWebForms
{
    public partial class OrdersTableControl : UserControl
    {
        private Dictionary<int, List<OrderItem>> _itemsCache = new();

        public bool ShowDeleted { get; set; }

        public void Bind(IEnumerable<Order> orders)
        {
            _itemsCache = orders.ToDictionary(o => o.Id, o => o.Items);
            tableWrap.Visible = true;
            phUnavailable.Visible = false;
            rptTable.DataSource = orders;
            rptTable.DataBind();
        }

        public void ShowServiceUnavailable(string serviceName)
        {
            tableWrap.Visible = false;
            phUnavailable.Visible = true;
            litUnavailable.Text = UiHelper.ServiceUnavailableMessage(serviceName);
        }

        protected string RowStyle(object isDeletedObj)
        {
            bool deleted = isDeletedObj is bool && (bool)isDeletedObj;
            return ShowDeleted && deleted ? "opacity:0.45;text-decoration:line-through" : "";
        }

        protected string ItemNames(int orderId)
        {
            List<OrderItem> items;
            if (_itemsCache.TryGetValue(orderId, out items))
                return UiHelper.FormatItemNames(items);
            try
            {
                return UiHelper.FormatItemNames(AppData.Services.Orders.GetItems(orderId));
            }
            catch (Exception ex) when (UiHelper.IsTransportFailure(ex))
            {
                return "<span style='color:#9a9790'>Orders unavailable</span>";
            }
        }

        protected string StatusBadge(object isDeletedObj, object statusObj)
        {
            bool deleted = isDeletedObj is bool && (bool)isDeletedObj;
            if (ShowDeleted && deleted) return UiHelper.GetStatusBadge("Deleted");
            return UiHelper.GetStatusBadge(statusObj != null ? statusObj.ToString() : "");
        }
    }
}
