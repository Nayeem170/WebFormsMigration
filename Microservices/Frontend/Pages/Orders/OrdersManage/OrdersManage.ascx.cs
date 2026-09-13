using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CoreWebForms
{
    public partial class OrdersManageControl : UserControl
    {
        public event EventHandler OrderDeleted = default!;
        private Dictionary<int, List<OrderItem>> _itemsCache = new();

        public void Bind()
        {
            string field = ViewState["OrderSortField"]?.ToString() ?? "OrderDate";
            string dir = ViewState["OrderDir"]?.ToString() ?? "DESC";
            string status = ddlStatusFilter.SelectedValue;

            var orders = AppData.Services.Orders.GetAll(false, status);

            _itemsCache = orders.ToDictionary(o => o.Id, o => o.Items);

            IEnumerable<Order> sorted = field switch
            {
                "CustomerName" => dir == "ASC" ? orders.OrderBy(o => o.CustomerName) : orders.OrderByDescending(o => o.CustomerName),
                "Total"        => dir == "ASC" ? orders.OrderBy(o => o.Total)        : orders.OrderByDescending(o => o.Total),
                "Status"       => dir == "ASC" ? orders.OrderBy(o => o.Status)       : orders.OrderByDescending(o => o.Status),
                "Priority"     => dir == "ASC" ? orders.OrderBy(o => o.Priority)     : orders.OrderByDescending(o => o.Priority),
                "OrderDate"    => dir == "ASC" ? orders.OrderBy(o => o.OrderDate)    : orders.OrderByDescending(o => o.OrderDate),
                _              => dir == "ASC" ? orders.OrderBy(o => o.Id)           : orders.OrderByDescending(o => o.Id),
            };

            var list = sorted.ToList();
            lblOrderCount.Text = string.Format("{0} order{1}", list.Count, list.Count != 1 ? "s" : "");
            lblOrderError.Visible = false;
            gvOrders.DataSource = list;
            gvOrders.DataBind();
            upOrders.Update();
        }

        protected void ddlStatusFilter_Changed(object sender, EventArgs e)
        {
            gvOrders.EditIndex = -1;
            gvOrders.PageIndex = 0;
            Bind();
        }

        protected void ddlPageSizeO_Changed(object sender, EventArgs e)
        {
            if (!int.TryParse(ddlPageSizeO.SelectedValue, out int pageSize) || pageSize < 1) pageSize = 5;
            gvOrders.PageSize = pageSize;
            gvOrders.EditIndex = -1;
            gvOrders.PageIndex = 0;
            Bind();
        }

        protected void gvOrders_RowCreated(object sender, GridViewRowEventArgs e)
        {
            GridViewHelper.ApplySortArrow(gvOrders, e, "OrderSortField", "OrderDir", ViewState);
        }

        protected void gvOrders_Sorting(object sender, GridViewSortEventArgs e)
        {
            GridViewHelper.ToggleSortDirection(e, "OrderSortField", "OrderDir", ViewState);
            gvOrders.EditIndex = -1;
            gvOrders.PageIndex = 0;
            Bind();
        }

        protected void gvOrders_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvOrders.EditIndex = -1;
            gvOrders.PageIndex = e.NewPageIndex;
            Bind();
        }

        protected void gvOrders_RowEditing(object sender, GridViewEditEventArgs e)
        {
            gvOrders.EditIndex = e.NewEditIndex;
            Bind();
        }

        protected void gvOrders_RowUpdating(object sender, GridViewUpdateEventArgs e)
        {
            var row = gvOrders.Rows[e.RowIndex];
            int id = (int)gvOrders.DataKeys[e.RowIndex].Value;
            var order = AppData.Services.Orders.GetById(id);
            if (order == null) { gvOrders.EditIndex = -1; Bind(); return; }

            order.Status   = ((DropDownList)row.FindControl("ddlEditStatus")).SelectedValue;
            order.Priority = ((DropDownList)row.FindControl("ddlEditPriority")).SelectedValue;

            string? updateError = null;
            try
            {
                AppData.Services.Orders.UpdateStatus(order.Id, order.Status, order.Priority);
            }
            catch (Exception ex)
            {
                updateError = "An error occurred while saving. Please try again.";
                AppData.Services.Log.Error(string.Format("Failed to update order #{0}", id), ex);
            }

            gvOrders.EditIndex = -1;
            Bind();
            if (updateError != null)
            {
                lblOrderError.Text = updateError;
                lblOrderError.Visible = true;
            }
        }

        protected void gvOrders_RowDataBound(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType != DataControlRowType.DataRow) return;

            if (e.Row.RowState == DataControlRowState.Edit ||
                e.Row.RowState == (DataControlRowState.Edit | DataControlRowState.Alternate))
            {
                var order = e.Row.DataItem as Order;
                if (order == null) return;
                var statusItem = ((DropDownList)e.Row.FindControl("ddlEditStatus")).Items.FindByValue(order.Status);
                if (statusItem != null) statusItem.Selected = true;
                var prioItem = ((DropDownList)e.Row.FindControl("ddlEditPriority")).Items.FindByValue(order.Priority);
                if (prioItem != null) prioItem.Selected = true;
            }
            else
            {
                foreach (DataControlFieldCell cell in e.Row.Cells)
                {
                    foreach (Control ctrl in cell.Controls)
                    {
                        if (ctrl is LinkButton lb && lb.CommandName == "Delete")
                        {
                            int id    = (int)gvOrders.DataKeys[e.Row.RowIndex].Value;
                            var order = e.Row.DataItem as Order;

                            if (order != null && order.Status == AppConstants.OrderStatus.Delivered)
                            {
                                lb.Enabled = false;
                                lb.Style["text-decoration"] = "none";
                                lb.Style["color"] = "#ccc";
                                lb.ToolTip = "Delivered orders cannot be deleted";
                                continue;
                            }
                            lb.OnClientClick = string.Format(
                                "showConfirm('Are you sure you want to delete order #{0}?', function(){{ document.getElementById('{1}').value={0}; document.getElementById('{2}').click(); }}); return false;",
                                id, hdnDeleteOrderId.ClientID, btnConfirmDelete.ClientID);
                        }
                    }
                }
            }
        }

        protected void gvOrders_RowCancelingEdit(object sender, GridViewCancelEditEventArgs e)
        {
            gvOrders.EditIndex = -1;
            Bind();
        }

        protected void gvOrders_RowDeleting(object sender, GridViewDeleteEventArgs e)
        {
            e.Cancel = true;
        }

        protected void btnConfirmDelete_Click(object sender, EventArgs e)
        {
            if (int.TryParse(hdnDeleteOrderId.Value, out int id) && id > 0)
            {
                var order = AppData.Services.Orders.GetById(id);
                if (order != null && order.Status == AppConstants.OrderStatus.Delivered)
                {
                    hdnDeleteOrderId.Value = "";
                    return;
                }
                string? deleteError = null;
                try { AppData.Services.Orders.DeleteOrder(id); }
                catch (Exception ex)
                {
                    AppData.Services.Log.Error(string.Format("Failed to delete order #{0}", id), ex);
                    deleteError = "Failed to delete order. Please try again.";
                }
                gvOrders.PageIndex = 0;
                Bind();
                if (deleteError != null)
                {
                    lblOrderError.Text = deleteError;
                    lblOrderError.Visible = true;
                }
                else
                {
                    OrderDeleted?.Invoke(this, EventArgs.Empty);
                }
            }
            hdnDeleteOrderId.Value = "";
        }

        protected string StatusBadge(object statusObj)
        {
            return UiHelper.GetStatusBadge(statusObj != null ? statusObj.ToString() : "");
        }

        protected string GetItemNames(int orderId)
        {
            if (_itemsCache.TryGetValue(orderId, out var items))
                return UiHelper.FormatItemNames(items);
            return UiHelper.FormatItemNames(AppData.Services.Orders.GetItems(orderId));
        }
    }
}
