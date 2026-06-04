using System;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public partial class ProductDetailControl : UserControl
    {
        public event EventHandler DetailClosed = default!;

        public void Show(Product product)
        {
            if (product == null) { Visible = false; return; }
            detId.Text     = product.Id.ToString();
            detName.Text   = System.Web.HttpUtility.HtmlEncode(product.Name);
            detCat.Text    = System.Web.HttpUtility.HtmlEncode(product.Category);
            detPrice.Text  = product.Price.ToString("C");
            detStatus.Text = product.IsActive
                ? "<span class='badge b-active'>Active</span>"
                : "<span class='badge badge-gray'>Inactive</span>";
            detStock.Text = product.Stock == 0
                ? "<span class='badge b-oos'>Out of stock</span>"
                : product.Stock <= 5
                    ? string.Format("<span class='badge b-low'>{0} units</span>", product.Stock)
                    : string.Format("{0} units", product.Stock);
            detAdded.Text  = product.AddedDate.ToString("d");
            pnlDetail.Visible = true;
        }

        public void Hide()
        {
            pnlDetail.Visible = false;
        }

        protected void lnkCloseDetail_Click(object sender, EventArgs e)
        {
            pnlDetail.Visible = false;
            DetailClosed?.Invoke(this, EventArgs.Empty);
        }
    }
}
