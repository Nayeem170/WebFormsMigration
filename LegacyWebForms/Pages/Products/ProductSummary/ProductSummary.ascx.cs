using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public partial class ProductSummaryControl : UserControl
    {
        public void Bind()
        {
            var all = AppData.Services.Products.GetAll();
            litProdTotal.Text  = all.Count.ToString();
            litProdActive.Text = all.Count(p => p.IsActive && p.Stock > 0).ToString();
            litProdLow.Text    = all.Count(p => p.Stock > 0 && p.Stock <= 5).ToString();
            litProdOos.Text    = all.Count(p => p.Stock == 0).ToString();
        }
    }
}
