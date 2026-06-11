using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;
using CoreWebForms.Services;

namespace CoreWebForms
{
    public partial class ProductsPage : AppPage
    {
        private readonly ProductService _products = AppData.Services.Products;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                BindFilterDropDown();
                ViewState["SortField"] = "Id";
                ViewState["SortDir"]   = "ASC";
                BindGrid();
            }
        }

        private void BindFilterDropDown()
        {
            ddlFilter.Items.Clear();
            ddlFilter.Items.Add(new ListItem("All Categories", ""));
            foreach (var cat in AppConstants.Categories)
                ddlFilter.Items.Add(new ListItem(cat, cat));
        }

        private void BindGrid()
        {
            string field     = ViewState["SortField"]?.ToString() ?? "Id";
            string dir       = ViewState["SortDir"]?.ToString() ?? "ASC";
            string cat        = ddlFilter.SelectedValue;
            string activeVal  = ddlActiveFilter.SelectedValue;

            var all = _products.GetAll(includeDeleted: activeVal == "inactive");
            productSummary.Bind(activeVal == "inactive" ? _products.GetAll() : all);

            var query = all.AsEnumerable();
            if (!string.IsNullOrEmpty(cat)) query = query.Where(p => p.Category == cat);
            if (activeVal == "active")        query = query.Where(p => p.IsActive && p.Stock > 0);
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
            lblRowCount.Text      = string.Format("{0} product{1}", list.Count, list.Count != 1 ? "s" : "");
            gvProducts.DataSource = list;
            gvProducts.DataBind();
        }

        protected void ddlFilter_Changed(object sender, EventArgs e)
        {
            gvProducts.EditIndex = -1;
            gvProducts.PageIndex = 0;
            productDetail.Hide();
            BindGrid();
        }

        protected void ddlPageSizeP_Changed(object sender, EventArgs e)
        {
            if (!int.TryParse(ddlPageSizeP.SelectedValue, out int pageSize) || pageSize < 1) pageSize = 5;
            gvProducts.PageSize = pageSize;
            gvProducts.EditIndex = -1;
            gvProducts.PageIndex = 0;
            productDetail.Hide();
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
                    foreach (var cat in AppConstants.Categories)
                        ddl.Items.Add(new ListItem(cat, cat));
            }

            GridViewHelper.ApplySortArrow(gvProducts, e, "SortField", "SortDir", ViewState);
        }

        protected void gvProducts_Sorting(object sender, GridViewSortEventArgs e)
        {
            GridViewHelper.ToggleSortDirection(e, "SortField", "SortDir", ViewState);
            gvProducts.EditIndex = -1;
            gvProducts.PageIndex = 0;
            BindGrid();
        }

        protected void gvProducts_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvProducts.EditIndex = -1;
            gvProducts.PageIndex = e.NewPageIndex;
            BindGrid();
        }

        protected void gvProducts_SelectedIndexChanged(object sender, EventArgs e)
        {
            int id = (int)gvProducts.SelectedDataKey.Value;
            var p  = _products.GetById(id);
            if (p == null) return;
            productDetail.Show(p);
        }

        protected void gvProducts_RowEditing(object sender, GridViewEditEventArgs e)
        {
            gvProducts.EditIndex = e.NewEditIndex;
            productDetail.Hide();
            BindGrid();
        }

        protected void gvProducts_RowUpdating(object sender, GridViewUpdateEventArgs e)
        {
            var row = gvProducts.Rows[e.RowIndex];
            int id  = (int)gvProducts.DataKeys[e.RowIndex].Value;
            var p   = _products.GetById(id);
            if (p == null)
            {
                gvProducts.EditIndex = -1;
                BindGrid();
                return;
            }

            p.Name     = ((TextBox)row.FindControl("txtEditName")).Text.Trim();
            p.Category = ((DropDownList)row.FindControl("ddlEditCategory")).SelectedValue;
            p.IsActive = ((CheckBox)row.FindControl("chkEditActive")).Checked;

            decimal price;
            if (!decimal.TryParse(((TextBox)row.FindControl("txtEditPrice")).Text, out price) || price < 0)
            {
                gvProducts.EditIndex = -1;
                BindGrid();
                lblRowCount.Text = "Price must be a valid non-negative number. Update cancelled.";
                return;
            }
            p.Price = price;

            int stock;
            if (!int.TryParse(((TextBox)row.FindControl("txtEditStock")).Text, out stock) || stock < 0)
            {
                gvProducts.EditIndex = -1;
                BindGrid();
                lblRowCount.Text = "Stock must be a valid non-negative number. Update cancelled.";
                return;
            }
            p.Stock = stock;

            string? errorMsg = null;
            try
            {
                _products.Update(p);
            }
            catch (Exception ex)
            {
                AppData.Services.Log.Error(string.Format("Failed to update product #{0}", id), ex);
                errorMsg = "An error occurred while saving. Please try again.";
            }

            gvProducts.EditIndex = -1;
            productDetail.Hide();
            BindGrid();

            if (errorMsg != null)
                lblRowCount.Text = errorMsg;
        }

        protected void gvProducts_RowCancelingEdit(object sender, GridViewCancelEditEventArgs e)
        {
            gvProducts.EditIndex = -1;
            BindGrid();
        }

        protected void addProduct_ProductAdded(object sender, ProductEventArgs e)
        {
            BindGrid();
        }

        protected void productDetail_DetailClosed(object sender, EventArgs e)
        {
            gvProducts.SelectedIndex = -1;
        }

        protected static string GetStatusHtml(object dataItem)
        {
            var p = dataItem as Product;
            if (p == null) return "";
            if (p.IsDeleted) return "<span class='badge badge-gray'>Deleted</span>";
            if (p.IsActive && p.Stock > 0) return "<span class='badge b-active'>Active</span>";
            return "<span class='badge badge-gray'>Inactive</span>";
        }
    }
}
