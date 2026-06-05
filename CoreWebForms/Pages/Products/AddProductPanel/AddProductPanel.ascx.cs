using System;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public partial class AddProductPanelControl : UserControl
    {
        public event EventHandler<ProductEventArgs> ProductAdded = default!;

        private void BindCategoryDropDown()
        {
            ddlNewCategory.Items.Clear();
            ddlNewCategory.Items.Add(new ListItem("-- Select --", ""));
            foreach (var cat in AppConstants.Categories)
                ddlNewCategory.Items.Add(new ListItem(cat, cat));
        }

        protected void lnkAddProduct_Click(object sender, EventArgs e)
        {
            pnlAdd.Visible = !pnlAdd.Visible;
            if (pnlAdd.Visible)
            {
                BindCategoryDropDown();
                lblAddResult.Visible = false;
            }
        }

        protected void btnSaveNew_Click(object sender, EventArgs e)
        {
            if (!Page.IsValid) return;

            lblAddResult.Visible = false;

            decimal price;
            int stock;
            if (!decimal.TryParse(txtNewPrice.Text, out price) || price < 0)
            {
                lblAddResult.Text = "Invalid price value.";
                lblAddResult.CssClass = "alert alert-warn mb";
                lblAddResult.Visible = true;
                return;
            }
            if (!int.TryParse(txtNewStock.Text, out stock) || stock < 0)
            {
                lblAddResult.Text = "Invalid stock value.";
                lblAddResult.CssClass = "alert alert-warn mb";
                lblAddResult.Visible = true;
                return;
            }

            var p = new Product
            {
                Name      = txtNewName.Text.Trim(),
                Category  = ddlNewCategory.SelectedValue,
                Price     = price,
                Stock     = stock,
                IsActive  = stock > 0 && chkNewActive.Checked,
                AddedDate = DateTime.UtcNow
            };
            int id;
            try
            {
                id = AppData.Services.Products.Add(p);
            }
            catch (Exception ex)
            {
                AppData.Services.Log.Error("Failed to add product", ex);
                lblAddResult.Text    = "An error occurred while saving. Please try again.";
                lblAddResult.CssClass = "alert alert-warn mb";
                lblAddResult.Visible = true;
                return;
            }

            txtNewName.Text      = "";
            txtNewPrice.Text     = "";
            txtNewStock.Text     = "";
            ddlNewCategory.SelectedIndex = 0;
            chkNewActive.Checked = true;
            lblAddResult.Text    = string.Format("Product #{0} \"{1}\" added.", id, System.Web.HttpUtility.HtmlEncode(p.Name));
            lblAddResult.CssClass = "alert alert-success mb";
            lblAddResult.Visible = true;

            ProductAdded?.Invoke(this, new ProductEventArgs { ProductId = id, ProductName = p.Name });
        }

        protected void btnCancelNew_Click(object sender, EventArgs e)
        {
            pnlAdd.Visible = false;
        }
    }
}
