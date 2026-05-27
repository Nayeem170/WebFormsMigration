using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace LegacyWebForms
{
    public static class UiHelper
    {
        public static string GetStatusBadge(string status)
        {
            string encoded = HttpUtility.HtmlEncode(status ?? "");
            if (status == AppConstants.OrderStatus.Delivered)
                return string.Format("<span class='badge b-delivered'>{0}</span>", encoded);
            if (status == AppConstants.OrderStatus.Shipped)
                return string.Format("<span class='badge b-shipped'>{0}</span>", encoded);
            if (status == AppConstants.OrderStatus.Processing)
                return string.Format("<span class='badge b-processing'>{0}</span>", encoded);
            if (status == AppConstants.UiLabels.Deleted)
                return string.Format("<span class='badge badge-gray'>{0}</span>", encoded);
            return string.Format("<span class='badge b-pending'>{0}</span>", encoded);
        }

        public static string FormatItemNames(IEnumerable<OrderItem> items)
        {
            return string.Join("<br/>", items.Select(i => HttpUtility.HtmlEncode(
                i.Quantity > 1 ? string.Format("{0} x{1}", i.ProductName, i.Quantity) : i.ProductName)));
        }
    }
}
