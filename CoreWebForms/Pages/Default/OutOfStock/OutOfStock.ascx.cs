using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CoreWebForms
{
    public partial class OutOfStockControl : UserControl
    {
        public void Bind(List<Product> products)
        {
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
    }
}
