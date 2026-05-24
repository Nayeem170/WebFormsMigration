using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public partial class OrdersPage : BasePage
    {
        private List<OrderItem> CartItems
        {
            get { return (List<OrderItem>)(ViewState["CartItems"] ?? (ViewState["CartItems"] = new List<OrderItem>())); }
            set { ViewState["CartItems"] = value; }
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                ViewState["OrderSortField"] = "OrderDate";
                ViewState["OrderSortDir"]   = "DESC";
                BindProductDropDown();
                BindHistory();
                BindOrdersGrid();
                var def = DefaultDeliveryDate();
                calDelivery.SelectedDate = def;
                calDelivery.VisibleDate  = def;
            }
        }

        private static DateTime DefaultDeliveryDate() => DateTime.Today;

        protected void btnCalToday_Click(object sender, EventArgs e)
        {
            calDelivery.SelectedDate = DateTime.Today;
            calDelivery.VisibleDate  = DateTime.Today;
            upCalendar.Update();
        }

        private void BindProductDropDown()
        {
            ddlProduct.Items.Clear();
            ddlProduct.Items.Add(new ListItem("-- Select product --", ""));
            foreach (var p in AppData.GetProducts().Where(x => x.IsActive && x.Stock > 0))
                ddlProduct.Items.Add(new ListItem(
                    string.Format("{0}  (${1:F2})", p.Name, p.Price),
                    p.Id.ToString()));
            ddlProduct.SelectedIndex = 0;
        }

        private void BindCartItems()
        {
            var items = CartItems;
            rptCart.DataSource = items;
            rptCart.DataBind();
            pnlCart.Visible = items.Count > 0;

            decimal total = items.Sum(x => x.LineTotal);
            litTotal.Text = items.Count > 0 ? string.Format("${0:F2}", total) : "-";
        }

        private const int HistoryPageSize = 10;

        private int HistoryPage
        {
            get { return (int)(ViewState["HistoryPage"] ?? 0); }
            set { ViewState["HistoryPage"] = value; }
        }

        private void BindHistory()
        {
            var all = AppData.GetOrders(includeDeleted: true);
            int total = all.Count;
            int pages = (int)Math.Ceiling((double)total / HistoryPageSize);
            if (HistoryPage >= pages && pages > 0) HistoryPage = pages - 1;
            if (HistoryPage < 0) HistoryPage = 0;

            var page = all.Skip(HistoryPage * HistoryPageSize).Take(HistoryPageSize).ToList();
            rptHistory.DataSource = page;
            rptHistory.DataBind();

            lblHistPage.Text = pages > 0 ? string.Format("{0} / {1}", HistoryPage + 1, pages) : "";
            lnkHistPrev.Visible = HistoryPage > 0;
            lnkHistNext.Visible = HistoryPage < pages - 1;
            upHistory.Update();
        }

        protected void lnkHistPrev_Click(object sender, EventArgs e)
        {
            HistoryPage--;
            BindHistory();
        }

        protected void lnkHistNext_Click(object sender, EventArgs e)
        {
            HistoryPage++;
            BindHistory();
        }

        private void BindOrdersGrid()
        {
            string field  = ViewState["OrderSortField"].ToString();
            string dir    = ViewState["OrderSortDir"].ToString();
            string status = ddlStatusFilter.SelectedValue;
            var orders    = AppData.GetOrders();
            if (!string.IsNullOrEmpty(status))
                orders = orders.Where(o => o.Status == status).ToList();

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
            lblOrderCount.Text    = string.Format("{0} order{1}", list.Count, list.Count != 1 ? "s" : "");
            gvOrders.DataSource   = list;
            gvOrders.DataBind();
            upOrders.Update();
        }

        protected void ddlStatusFilter_Changed(object sender, EventArgs e)
        {
            gvOrders.PageIndex = 0;
            BindOrdersGrid();
        }

        protected void ddlPageSizeO_Changed(object sender, EventArgs e)
        {
            gvOrders.PageSize  = int.Parse(ddlPageSizeO.SelectedValue);
            gvOrders.PageIndex = 0;
            BindOrdersGrid();
        }

        protected void gvOrders_RowCreated(object sender, GridViewRowEventArgs e)
        {
            ApplySortArrow(gvOrders, e, "OrderSortField", "OrderSortDir");
        }

        protected void gvOrders_Sorting(object sender, GridViewSortEventArgs e)
        {
            ToggleSortDirection(e, "OrderSortField", "OrderSortDir");
            gvOrders.PageIndex = 0;
            BindOrdersGrid();
        }

        protected void gvOrders_PageIndexChanging(object sender, GridViewPageEventArgs e)
        {
            gvOrders.PageIndex = e.NewPageIndex;
            BindOrdersGrid();
        }

        protected void gvOrders_RowEditing(object sender, GridViewEditEventArgs e)
        {
            gvOrders.EditIndex = e.NewEditIndex;
            BindOrdersGrid();
        }

        protected void gvOrders_RowUpdating(object sender, GridViewUpdateEventArgs e)
        {
            var row    = gvOrders.Rows[e.RowIndex];
            int id     = (int)gvOrders.DataKeys[e.RowIndex].Value;
            var order  = AppData.GetOrder(id);
            if (order == null) { gvOrders.EditIndex = -1; BindOrdersGrid(); return; }

            order.Status   = ((DropDownList)row.FindControl("ddlEditStatus")).SelectedValue;
            order.Priority = ((DropDownList)row.FindControl("ddlEditPriority")).SelectedValue;
            AppData.UpdateOrder(order);

            gvOrders.EditIndex = -1;
            BindHistory();
            BindOrdersGrid();
        }

        protected void gvOrders_RowDataBound(object sender, GridViewRowEventArgs e)
        {
            if (e.Row.RowType != DataControlRowType.DataRow) return;

            if (e.Row.RowState == DataControlRowState.Edit ||
                e.Row.RowState == (DataControlRowState.Edit | DataControlRowState.Alternate))
            {
                var order = (Order)e.Row.DataItem;
                ((DropDownList)e.Row.FindControl("ddlEditStatus")).Items.FindByValue(order.Status).Selected     = true;
                ((DropDownList)e.Row.FindControl("ddlEditPriority")).Items.FindByValue(order.Priority).Selected = true;
            }
            else
            {
                foreach (DataControlFieldCell cell in e.Row.Cells)
                {
                    foreach (Control ctrl in cell.Controls)
                    {
                        if (ctrl is LinkButton lb && lb.CommandName == "Delete")
                        {
                            int id = (int)gvOrders.DataKeys[e.Row.DataItemIndex].Value;
                            var order = (Order)e.Row.DataItem;
                            if (order != null && order.Status == "Delivered")
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
            BindOrdersGrid();
        }

        protected void gvOrders_RowDeleting(object sender, GridViewDeleteEventArgs e)
        {
            int id = (int)gvOrders.DataKeys[e.RowIndex].Value;
            AppData.DeleteOrder(id);
            gvOrders.PageIndex = 0;
            BindHistory();
            BindOrdersGrid();
        }

        protected void btnConfirmDelete_Click(object sender, EventArgs e)
        {
            int id;
            if (int.TryParse(hdnDeleteOrderId.Value, out id) && id > 0)
            {
                var order = AppData.GetOrder(id);
                if (order != null && order.Status == "Delivered")
                {
                    hdnDeleteOrderId.Value = "";
                    return;
                }
                AppData.DeleteOrder(id);
                gvOrders.PageIndex = 0;
                BindHistory();
                BindOrdersGrid();
            }
            hdnDeleteOrderId.Value = "";
        }

        protected string GetItemNames(int orderId)
        {
            var items = AppData.GetOrderItems(orderId);
            return string.Join("<br/>", items.Select(i => Server.HtmlEncode(
                i.Quantity > 1 ? string.Format("{0} x{1}", i.ProductName, i.Quantity) : i.ProductName)));
        }

        protected string GetItemsCount(int orderId)
        {
            var items = AppData.GetOrderItems(orderId);
            return string.Format("{0} item{1}", items.Count, items.Count != 1 ? "s" : "");
        }

        // ── Cart actions ──────────────────────────────────────────────────────

        protected void btnAddItem_Click(object sender, EventArgs e)
        {
            int productId;
            if (!int.TryParse(ddlProduct.SelectedValue, out productId) || productId == 0) return;
            var product = AppData.GetProduct(productId);
            if (product == null) return;
            int qty;
            if (!int.TryParse(txtQty.Text, out qty) || qty < 1) qty = 1;

            int inCart = CartItems.Where(i => i.ProductId == productId).Sum(i => i.Quantity);
            int available = product.Stock - inCart;
            if (qty > available)
            {
                lblCartWarning.Text = available <= 0
                    ? string.Format("{0} is already in your cart with no more stock available.", product.Name)
                    : string.Format("Only {0} more unit{1} of {2} available (already {3} in cart).", available, available != 1 ? "s" : "", product.Name, inCart);
                lblCartWarning.Visible = true;
                return;
            }

            lblCartWarning.Visible = false;
            var items = CartItems;
            items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = qty,
                UnitPrice = product.Price
            });
            CartItems = items;
            BindCartItems();
            BindProductDropDown();
        }

        protected void rptCart_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "remove") return;
            int index = int.Parse(e.CommandArgument.ToString());
            var items = CartItems;
            items.RemoveAt(index);
            CartItems = items;
            BindCartItems();
        }

        // ── Wizard ──────────────────────────────────────────────────────────

        protected void btnNext_Click(object sender, EventArgs e)
        {
            if (!Page.IsValid) return;
            if (CartItems.Count == 0) return;

            decimal total = CartItems.Sum(x => x.LineTotal);
            rptRevItems.DataSource = CartItems;
            rptRevItems.DataBind();
            litRevTotal.Text    = string.Format("${0:F2}", total);
            litRevCustomer.Text = txtCustomerName.Text;
            litRevEmail.Text    = txtEmail.Text;
            litRevPriority.Text = rblPriority.SelectedValue;
            litRevDate.Text     = calDelivery.SelectedDate.ToString("D");
            litRevExtras.Text   = SelectedExtras().Count > 0 ? string.Join(", ", SelectedExtras()) : "None";
            SetStep(1);
        }

        protected void btnBack_Click(object sender, EventArgs e) => SetStep(0);

        protected void btnConfirm_Click(object sender, EventArgs e)
        {
            var items = CartItems;
            if (items.Count == 0) return;

            decimal total = items.Sum(x => x.LineTotal);
            var order = new Order
            {
                CustomerName  = txtCustomerName.Text.Trim(),
                CustomerEmail = txtEmail.Text.Trim(),
                OrderDate     = DateTime.Now,
                DeliveryDate  = calDelivery.SelectedDate,
                Status        = "Pending",
                Priority      = rblPriority.SelectedValue,
                Extras        = SelectedExtras(),
                Total         = total,
                Items         = items
            };

            int id = AppData.AddOrder(order);
            litOrderId.Text = string.Format("#{0}", id);

            rptConfirmItems.DataSource = items;
            rptConfirmItems.DataBind();
            litConfTotal.Text    = string.Format("{0:F2}", total);
            litConfCustomer.Text = order.CustomerName;
            litConfEmail.Text    = order.CustomerEmail;
            litConfDate.Text     = order.DeliveryDate.ToString("D");
            litConfPriority.Text = order.Priority;
            var extras = SelectedExtras();
            if (extras.Count > 0)
            {
                trConfExtras.Visible = true;
                litConfExtras.Text = string.Join(", ", extras);
            }

            BindHistory();
            BindOrdersGrid();
            SetStep(2);
        }

        protected void btnNewOrder_Click(object sender, EventArgs e)
        {
            txtCustomerName.Text = "";
            txtEmail.Text        = "";
            txtQty.Text          = "1";
            foreach (ListItem item in cblExtras.Items)
                item.Selected = false;
            rblPriority.Items.FindByValue("Normal").Selected = true;
            var def = DefaultDeliveryDate();
            calDelivery.SelectedDate = def;
            calDelivery.VisibleDate  = def;
            CartItems = new List<OrderItem>();
            BindCartItems();
            BindProductDropDown();
            SetStep(0);
        }

        protected void cvDate_ServerValidate(object source, ServerValidateEventArgs args)
        {
            args.IsValid = calDelivery.SelectedDate != DateTime.MinValue && calDelivery.SelectedDate >= DateTime.Today;
        }

        private List<string> SelectedExtras()
        {
            var list = new List<string>();
            foreach (ListItem item in cblExtras.Items)
                if (item.Selected) list.Add(item.Text);
            return list;
        }

        private void SetStep(int step)
        {
            mvOrder.ActiveViewIndex = step;
            pnlStep1.CssClass = step == 0 ? "wt active" : "wt done";
            pnlStep2.CssClass = step == 1 ? "wt active" : step > 1 ? "wt done" : "wt";
            pnlStep3.CssClass = step == 2 ? "wt active" : "wt";
        }
    }
}
