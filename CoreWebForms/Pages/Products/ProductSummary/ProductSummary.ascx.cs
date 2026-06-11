using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CoreWebForms
{
    public partial class ProductSummaryControl : UserControl
    {
        public void Bind(List<Product> products)
        {
            litProdTotal.Text  = products.Count.ToString();
            litProdActive.Text = products.Count(p => p.IsActive && p.Stock > 0).ToString();
            litProdLow.Text    = products.Count(p => p.Stock > 0 && p.Stock <= 5).ToString();
            litProdOos.Text    = products.Count(p => p.Stock == 0).ToString();
        }
    }
}
