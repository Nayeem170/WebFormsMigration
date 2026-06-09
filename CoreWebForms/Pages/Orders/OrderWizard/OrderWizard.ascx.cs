using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace CoreWebForms
{
    public partial class OrderWizardControl : UserControl
    {
        private const string CartSessionKey = "CartItems";

        public event EventHandler<OrderEventArgs> OrderPlaced = default!;

        private List<OrderItem> CartItems
        {
            get
            {
                if (Session[CartSessionKey] == null)
                    Session[CartSessionKey] = new List<OrderItem>();
                return (List<OrderItem>)Session[CartSessionKey];
            }
            set { Session[CartSessionKey] = value; }
        }

        public void Bind()
        {
            BindProductDropDown();
            BindCartItems();
            var def = DateTime.Today;
            calDelivery.SelectedDate = def;
            calDelivery.VisibleDate = def;
        }

        private void BindProductDropDown()
        {
            ddlProduct.Items.Clear();
            ddlProduct.Items.Add(new ListItem("-- Select product --", ""));
            foreach (var p in AppData.Services.Products.GetAll().Where(x => x.IsActive && x.Stock > 0))
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

        protected void btnCalToday_Click(object sender, EventArgs e)
        {
            calDelivery.SelectedDate = DateTime.Today;
            calDelivery.VisibleDate = DateTime.Today;
        }

        protected void btnAddItem_Click(object sender, EventArgs e)
        {
            int productId;
            if (!int.TryParse(ddlProduct.SelectedValue, out productId) || productId == 0) return;
            var product = AppData.Services.Products.GetById(productId);
            if (product == null) return;
            int qty;
            if (!int.TryParse(txtQty.Text, out qty) || qty < 1) qty = 1;

            int inCart    = CartItems.Where(i => i.ProductId == productId).Sum(i => i.Quantity);
            int available = product.Stock - inCart;
            if (qty > available)
            {
                lblCartWarning.Text = available <= 0
                    ? string.Format("{0} is already in your cart with no more stock available.", System.Web.HttpUtility.HtmlEncode(product.Name))
                    : string.Format("Only {0} more unit{1} of {2} available (already {3} in cart).", available, available != 1 ? "s" : "", System.Web.HttpUtility.HtmlEncode(product.Name), inCart);
                lblCartWarning.Visible = true;
                return;
            }

            lblCartWarning.Visible = false;
            var items = CartItems;
            items.Add(new OrderItem
            {
                ProductId   = product.Id,
                ProductName = product.Name,
                Quantity    = qty,
                UnitPrice   = product.Price
            });
            CartItems = items;
            BindCartItems();
            BindProductDropDown();
        }

        protected void rptCart_ItemCommand(object source, RepeaterCommandEventArgs e)
        {
            if (e.CommandName != "remove") return;
            if (!int.TryParse(e.CommandArgument.ToString(), out int productId)) return;
            var items = CartItems;
            int idx = items.FindIndex(i => i.ProductId == productId);
            if (idx < 0) return;
            items.RemoveAt(idx);
            CartItems = items;
            BindCartItems();
        }

        protected void btnNext_Click(object sender, EventArgs e)
        {
            if (!Page.IsValid) return;
            if (calDelivery.SelectedDate == DateTime.MinValue || calDelivery.SelectedDate < DateTime.Today)
            {
                lblDateError.Visible = true;
                return;
            }
            lblDateError.Visible = false;
            if (CartItems.Count == 0)
            {
                lblCartWarning.Text = "Add at least one product before continuing.";
                lblCartWarning.Visible = true;
                return;
            }

            decimal total = CartItems.Sum(x => x.LineTotal);
            rptRevItems.DataSource = CartItems;
            rptRevItems.DataBind();
            litRevTotal.Text    = string.Format("${0:F2}", total);
            litRevCustomer.Text = System.Web.HttpUtility.HtmlEncode(txtCustomerName.Text);
            litRevEmail.Text    = System.Web.HttpUtility.HtmlEncode(txtEmail.Text);
            litRevPriority.Text = System.Web.HttpUtility.HtmlEncode(rblPriority.SelectedValue);
            litRevDate.Text     = calDelivery.SelectedDate.ToString("D");
            var reviewExtras = SelectedExtras();
            litRevExtras.Text = reviewExtras.Count > 0
                ? string.Join(", ", reviewExtras.Select(x => System.Web.HttpUtility.HtmlEncode(x)))
                : "None";
            SetStep(1);
        }

        protected void btnBack_Click(object sender, EventArgs e) => SetStep(0);

        protected void btnConfirm_Click(object sender, EventArgs e)
        {
            var items = CartItems;
            if (items.Count == 0) return;

            var cart = items.ToList();
            decimal total = 0;
            foreach (var item in cart)
            {
                var product = AppData.Services.Products.GetById(item.ProductId);
                if (product == null || product.IsDeleted)
                {
                    lblCartWarning.Text = string.Format("Product \"{0}\" is no longer available.", System.Web.HttpUtility.HtmlEncode(item.ProductName));
                    lblCartWarning.Visible = true;
                    SetStep(0);
                    return;
                }
                if (product.Stock < item.Quantity)
                {
                    lblCartWarning.Text = string.Format("Only {0} units of \"{1}\" available (you have {2} in cart).", product.Stock, System.Web.HttpUtility.HtmlEncode(product.Name), item.Quantity);
                    lblCartWarning.Visible = true;
                    SetStep(0);
                    return;
                }
                item.UnitPrice = product.Price;
                total += item.UnitPrice * item.Quantity;
            }

            var order = new Order
            {
                CustomerName  = txtCustomerName.Text.Trim(),
                CustomerEmail = txtEmail.Text.Trim(),
                OrderDate     = DateTime.UtcNow,
                DeliveryDate  = calDelivery.SelectedDate,
                Status        = AppConstants.OrderStatus.Pending,
                Priority      = rblPriority.SelectedValue,
                Extras        = SelectedExtras(),
                Items         = cart
            };

            int id;
            try
            {
                id = AppData.Services.Orders.PlaceOrder(order);
            }
            catch (Exception ex)
            {
                AppData.Services.Log.Error("Failed to place order", ex);
                lblCartWarning.Text = "An error occurred while placing your order. Please try again.";
                lblCartWarning.Visible = true;
                SetStep(0);
                return;
            }
            litOrderId.Text = string.Format("#{0}", id);

            rptConfirmItems.DataSource = cart;
            rptConfirmItems.DataBind();
            litConfTotal.Text    = string.Format("{0:F2}", total);
            litConfCustomer.Text = System.Web.HttpUtility.HtmlEncode(order.CustomerName);
            litConfEmail.Text    = System.Web.HttpUtility.HtmlEncode(order.CustomerEmail);
            litConfDate.Text     = order.DeliveryDate.ToString("D");
            litConfPriority.Text = System.Web.HttpUtility.HtmlEncode(order.Priority);
            var extras = order.Extras;
            if (extras.Count > 0)
            {
                trConfExtras.Visible = true;
                litConfExtras.Text   = string.Join(", ", extras.Select(x => System.Web.HttpUtility.HtmlEncode(x)));
            }

            SetStep(2);
            CartItems = new List<OrderItem>();
            OrderPlaced?.Invoke(this, new OrderEventArgs { OrderId = id });
        }

        protected void btnNewOrder_Click(object sender, EventArgs e)
        {
            txtCustomerName.Text = "";
            txtEmail.Text        = "";
            txtQty.Text          = "1";
            foreach (ListItem item in cblExtras.Items)
                item.Selected = false;
            var normalItem = rblPriority.Items.FindByValue("Normal");
            if (normalItem != null) normalItem.Selected = true;
            var def = DateTime.Today;
            calDelivery.SelectedDate = def;
            calDelivery.VisibleDate  = def;
            CartItems = new List<OrderItem>();
            BindCartItems();
            BindProductDropDown();
            SetStep(0);
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
