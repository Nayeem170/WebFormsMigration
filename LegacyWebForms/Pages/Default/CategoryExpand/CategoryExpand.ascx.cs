using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public partial class CategoryExpandControl : UserControl
    {
        public void Bind(List<Product> products)
        {
            BindCategoryExpand(products);
        }

        private void BindCategoryExpand(List<Product> products)
        {
            string? expanded = ViewState["ExpandedCat"] as string;
            rptCatExpand.DataSource = AppConstants.Categories.Select(cat => new
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
            string? cur = ViewState["ExpandedCat"] as string;
            ViewState["ExpandedCat"] = cur == cat ? null : cat;
            BindCategoryExpand(AppData.Services.Products.GetAll());
        }

        protected string GetProductList(object names)
        {
            var list = names as System.Collections.Generic.List<string>;
            if (list == null || list.Count == 0)
                return "<span style='color:#9a9790'>No active products.</span>";
            var sb = new StringBuilder();
            foreach (var n in list)
            {
                if (sb.Length > 0) sb.Append("<br>");
                sb.Append(HttpUtility.HtmlEncode(n));
            }
            return sb.ToString();
        }
    }
}
