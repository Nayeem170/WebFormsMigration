using System;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public partial class DefaultPage : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
                BindDashboard();
        }

        private void BindDashboard()
        {
            var products = AppData.GetProducts();
            var orders   = AppData.GetOrders();

            litTotalProducts.Text = products.Count.ToString();
            litLowStock.Text      = products.Count(p => p.Stock <= 5).ToString();
            litTotalOrders.Text   = orders.Count.ToString();
            litPending.Text       = orders.Count(o => o.Status == "Pending").ToString();

            rptOrders.DataSource = orders.Take(6).ToList();
            rptOrders.DataBind();

            BindCategoryExpand(products);

            var outOfStock = products.Where(p => p.Stock == 0).ToList();
            if (outOfStock.Count == 0)
            {
                rptOutOfStock.DataSource = null;
                rptOutOfStock.DataBind();
                pnlNoOos.Visible = true;
                litOosCount.Text = "";
            }
            else
            {
                rptOutOfStock.DataSource = outOfStock;
                rptOutOfStock.DataBind();
                pnlNoOos.Visible = false;
                litOosCount.Text = string.Format("<span class='badge b-oos' style='font-size:10px'>{0} item{1}</span>",
                    outOfStock.Count, outOfStock.Count != 1 ? "s" : "");
            }

        }
        private void BindCategoryExpand(System.Collections.Generic.List<Product> products = null)
        {
            if (products == null) products = AppData.GetProducts();
            string expanded = ViewState["ExpandedCat"] as string;
            rptCatExpand.DataSource = AppData.Categories.Select(cat => new
            {
                Category     = cat,
                Count        = products.Count(p => p.Category == cat && p.IsActive && p.Stock > 0),
                Expanded     = cat == expanded,
                ProductNames = products.Where(p => p.Category == cat && p.IsActive && p.Stock > 0)
                                       .Select(p => p.Name).ToList()
            }).ToList();
            rptCatExpand.DataBind();
        }

        protected void rptCatExpand_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "toggle") return;
            string cat = e.CommandArgument.ToString();
            string cur = ViewState["ExpandedCat"] as string;
            ViewState["ExpandedCat"] = cur == cat ? null : cat;
            BindCategoryExpand();
        }

        protected string GetProductList(object names)
        {
            var list = names as System.Collections.Generic.List<string>;
            if (list == null || list.Count == 0)
                return "<span style='color:#9a9790'>No active products.</span>";
            var sb = new System.Text.StringBuilder();
            foreach (var n in list)
            {
                if (sb.Length > 0) sb.Append("<br>");
                sb.Append(System.Web.HttpUtility.HtmlEncode(n));
            }
            return sb.ToString();
        }

        protected string GetOrderItemsSummary(int orderId)
        {
            var items = AppData.GetOrderItems(orderId);
            return string.Join("<br/>", items.Select(i => System.Web.HttpUtility.HtmlEncode(i.ProductName)));
        }
    }
}
