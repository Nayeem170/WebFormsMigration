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
            rptTable.DataSource = orders;
            rptTable.DataBind();
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
            return UiHelper.FormatItemNames(AppData.Services.Orders.GetItems(orderId));
        }

        protected string StatusBadge(object isDeletedObj, object statusObj)
        {
            bool deleted = isDeletedObj is bool && (bool)isDeletedObj;
            if (ShowDeleted && deleted) return UiHelper.GetStatusBadge("Deleted");
            return UiHelper.GetStatusBadge(statusObj != null ? statusObj.ToString() : "");
        }
    }
}
