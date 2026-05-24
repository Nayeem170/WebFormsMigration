using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public partial class ProductsPage : BasePage
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindFilterDropDown();
                BindCategoryDropDown(ddlNewCategory);
                ViewState["SortField"] = "Id";
                ViewState["SortDir"]   = "ASC";
                BindSummary();
                BindGrid();
            }
        }

        private void BindSummary()
        {
            var all = AppData.GetProducts();
            litProdTotal.Text  = all.Count.ToString();
            litProdActive.Text = all.Count(p => p.IsActive).ToString();
            litProdLow.Text    = all.Count(p => p.Stock > 0 && p.Stock <= 5).ToString();
            litProdOos.Text    = all.Count(p => p.Stock == 0).ToString();
        }

        private void BindFilterDropDown()
        {
            ddlFilter.Items.Clear();
            ddlFilter.Items.Add(new ListItem("All Categories", ""));
            foreach (var cat in AppData.Categories)
                ddlFilter.Items.Add(new ListItem(cat, cat));
        }

        private void BindCategoryDropDown(DropDownList ddl)
        {
            ddl.Items.Clear();
            ddl.Items.Add(new ListItem("-- Select --", ""));
            foreach (var cat in AppData.Categories)
                ddl.Items.Add(new ListItem(cat, cat));
        }

        private void BindGrid()
        {
            string field     = ViewState["SortField"].ToString();
            string dir       = ViewState["SortDir"].ToString();
            string cat        = ddlFilter.SelectedValue;
            string activeVal  = ddlActiveFilter.SelectedValue;

            var query = AppData.GetProducts(includeDeleted: activeVal == "inactive").AsEnumerable();
            if (!string.IsNullOrEmpty(cat)) query = query.Where(p => p.Category == cat);
            if (activeVal == "active")      query = query.Where(p => p.IsActive && p.Stock > 0);
            else if (activeVal == "inactive") query = query.Where(p => !p.IsActive || p.Stock == 0 || p.IsDeleted);

            IEnumerable<Product> sorted = field switch
            {
                "Name"     => dir == "ASC" ? query.OrderBy(p => p.Name)     : query.OrderByDescending(p => p.Name),
                "Category" => dir == "ASC" ? query.OrderBy(p => p.Category) : query.OrderByDescending(p => p.Category),
                "Price"    => dir == "ASC" ? query.OrderBy(p => p.Price)    : query.OrderByDescending(p => p.Price),
                "Stock"    => dir == "ASC" ? query.OrderBy(p => p.Stock)    : query.OrderByDescending(p => p.Stock),
                _          => dir == "ASC" ? query.OrderBy(p => p.Id)       : query.OrderByDescending(p => p.Id),
            };

            var list = sorted.ToList();
            lblRowCount.Text     = string.Format("{0} product{1}", list.Count, list.Count != 1 ? "s" : "");
            gvProducts.DataSource = list;
            gvProducts.DataBind();
        }

        protected void ddlFilter_Changed(object sender, EventArgs e)
        {
            gvProducts.PageIndex = 0;
            pnlDetail.Visible    = false;
            BindGrid();
        }

        protected void ddlPageSizeP_Changed(object sender, EventArgs e)
        {
            gvProducts.PageSize  = int.Parse(ddlPageSizeP.SelectedValue);
            gvProducts.PageIndex = 0;
            pnlDetail.Visible    = false;
            BindGrid();
        }

        protected void gvProducts_RowCreated(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType == DataControlRowType.DataRow &&
                (e.Row.RowState == DataControlRowState.Edit ||
                 e.Row.RowState == (DataControlRowState.Edit | DataControlRowState.Alternate)))
            {
                var ddl = (DropDownList)e.Row.FindControl("ddlEditCategory");
                if (ddl != null && ddl.Items.Count == 0)
                    foreach (var cat in AppData.Categories)
                        ddl.Items.Add(new ListItem(cat, cat));
            }

            ApplySortArrow(gvProducts, e, "SortField", "SortDir");
        }

        protected void gvProducts_Sorting(object sender, GridViewSortEventArgs e)
        {
            ToggleSortDirection(e, "SortField", "SortDir");
            gvProducts.PageIndex = 0;
            BindGrid();
        }

        protected void gvProducts_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvProducts.PageIndex = e.NewPageIndex;
            BindGrid();
        }

        protected void gvProducts_SelectedIndexChanged(object sender, EventArgs e)
        {
            int id = (int)gvProducts.SelectedDataKey.Value;
            var p  = AppData.GetProduct(id);
            if (p == null) return;
            detId.Text     = p.Id.ToString();
            detName.Text   = p.Name;
            detCat.Text    = p.Category;
            detPrice.Text  = p.Price.ToString("C");
            detStatus.Text = p.IsActive ? "<span class='badge b-active'>Active</span>" : "<span class='badge badge-gray'>Inactive</span>";
            if (p.Stock == 0)
                detStock.Text = "<span class='badge b-oos'>Out of stock</span>";
            else if (p.Stock <= 5)
                detStock.Text = string.Format("<span class='badge b-low'>{0} units</span>", p.Stock);
            else
                detStock.Text = string.Format("{0} units", p.Stock);
            detAdded.Text  = p.AddedDate.ToString("d");
            pnlDetail.Visible = true;
        }

        protected void lnkCloseDetail_Click(object sender, EventArgs e)
        {
            gvProducts.SelectedIndex = -1;
            pnlDetail.Visible = false;
        }

        protected void gvProducts_RowEditing(object sender, GridViewEditEventArgs e)
        {
            gvProducts.EditIndex = e.NewEditIndex;
            pnlDetail.Visible    = false;
            BindGrid();
        }

        protected void gvProducts_RowUpdating(object sender, GridViewUpdateEventArgs e)
        {
            var row = gvProducts.Rows[e.RowIndex];
            int id  = (int)gvProducts.DataKeys[e.RowIndex].Value;
            var p   = AppData.GetProduct(id);
            if (p == null)
            {
                gvProducts.EditIndex = -1;
                BindGrid();
                return;
            }

            p.Name     = Regex.Replace(((TextBox)row.FindControl("txtEditName")).Text.Trim(), "<.*?>", "");
            p.Category = ((DropDownList)row.FindControl("ddlEditCategory")).SelectedValue;
            p.IsActive = ((CheckBox)row.FindControl("chkEditActive")).Checked;

            decimal price;
            if (decimal.TryParse(((TextBox)row.FindControl("txtEditPrice")).Text, out price))
                p.Price = price;

            int stock;
            if (int.TryParse(((TextBox)row.FindControl("txtEditStock")).Text, out stock))
                p.Stock = stock;

            if (p.Stock == 0) p.IsActive = false;

            AppData.UpdateProduct(p);
            gvProducts.EditIndex = -1;
            BindSummary();
            BindGrid();
        }

        protected void gvProducts_RowCancelingEdit(object sender, GridViewCancelEditEventArgs e)
        {
            gvProducts.EditIndex = -1;
            BindGrid();
        }

        protected void gvProducts_RowDataBound(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType != DataControlRowType.DataRow) return;
            var product = (Product)e.Row.DataItem;
        }

        protected void lnkAddProduct_Click(object sender, EventArgs e)
        {
            pnlAdd.Visible = !pnlAdd.Visible;
            if (pnlAdd.Visible)
            {
                BindCategoryDropDown(ddlNewCategory);
                lblAddResult.Visible = false;
            }
        }

        protected void btnSaveNew_Click(object sender, EventArgs e)
        {
            if (!Page.IsValid) return;

            lblAddResult.Visible = false;

            decimal price;
            int     stock;
            if (!decimal.TryParse(txtNewPrice.Text, out price))
            {
                lblAddResult.Text = "Invalid price value.";
                lblAddResult.CssClass = "alert alert-warn mb";
                lblAddResult.Visible = true;
                return;
            }
            if (!int.TryParse(txtNewStock.Text, out stock))
            {
                lblAddResult.Text = "Invalid stock value.";
                lblAddResult.CssClass = "alert alert-warn mb";
                lblAddResult.Visible = true;
                return;
            }

            var p = new Product
            {
                Name     = txtNewName.Text.Trim(),
                Category = ddlNewCategory.SelectedValue,
                Price    = price,
                Stock    = stock,
                IsActive = stock > 0 && chkNewActive.Checked
            };
            int id = AppData.AddProduct(p);

            txtNewName.Text      = "";
            txtNewPrice.Text     = "";
            txtNewStock.Text     = "";
            chkNewActive.Checked = true;
            lblAddResult.Text    = string.Format("Product #{0} \"{1}\" added.", id, p.Name);
            lblAddResult.CssClass = "alert alert-success mb";
            lblAddResult.Visible = true;
            BindSummary();
            BindGrid();
        }

        protected void btnCancelNew_Click(object sender, EventArgs e)
        {
            pnlAdd.Visible = false;
        }
    }
}
