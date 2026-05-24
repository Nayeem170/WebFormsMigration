<%@ Page Title="Orders" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Orders.aspx.cs" Inherits="LegacyWebForms.OrdersPage" %>
<%@ Import Namespace="LegacyWebForms" %>

<asp:Content ID="HeadContent" ContentPlaceHolderID="HeadContent" runat="server">
</asp:Content>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
    <div class="ph">
        <div>
            <div class="pt">Orders</div>
            <div class="ps">Place and manage customer orders</div>
        </div>
    </div>

    <div class="orders-layout">

        <%-- Left: Wizard (MultiView) --%>
        <div class="card card-pad" id="wizardCard" runat="server">
            <div class="wizard-tabs">
                <asp:Panel ID="pnlStep1" runat="server" CssClass="wt active">1. Order info</asp:Panel>
                <asp:Panel ID="pnlStep2" runat="server" CssClass="wt">2. Review</asp:Panel>
                <asp:Panel ID="pnlStep3" runat="server" CssClass="wt">3. Confirmed</asp:Panel>
            </div>

            <asp:MultiView ID="mvOrder" runat="server" ActiveViewIndex="0">

                <%-- ===== Step 1: Order Info ===== --%>
                <asp:View ID="vStep1" runat="server">
                    <div>

                        <asp:ValidationSummary ID="vs1" runat="server" ValidationGroup="Step1"
                            CssClass="validation-summary" ShowSummary="true" ShowMessageBox="false" />

                        <%-- Products --%>
                        <div class="form-section-hdr">Products</div>
                        <div class="add-product-row">
                            <div class="combo-wrap" id="productCombo">
                                <div class="combo-trigger">
                                    <input type="text" id="txtProductInput" class="combo-input"
                                           placeholder="Select or search product..." autocomplete="off" />
                                    <span class="combo-arrow">&#9662;</span>
                                </div>
                                <div class="combo-list"></div>
                                <asp:DropDownList ID="ddlProduct" runat="server" style="display:none" />
                            </div>
                            <asp:TextBox ID="txtQty" runat="server" Width="60px" Text="1"
                                CssClass="qty-input" />
                            <asp:Button ID="btnAddItem" runat="server" Text="+ Add"
                                CssClass="btn btn-secondary" OnClick="btnAddItem_Click"
                                CausesValidation="false" />
                        </div>
                        <asp:Label ID="lblCartWarning" runat="server" CssClass="alert alert-warn mb" Visible="false" />

                        <asp:Panel ID="pnlCart" runat="server" Visible="false" CssClass="cart-panel">
                            <div class="cart-header">
                                <span class="ch-name">Product</span>
                                <span class="ch-qty">Qty</span>
                                <span class="ch-price">Price</span>
                                <span class="ch-action"></span>
                            </div>
                            <asp:Repeater ID="rptCart" runat="server" OnItemCommand="rptCart_ItemCommand">
                                <ItemTemplate>
                                    <div class="cart-row">
                                        <span class="ci-dot"></span>
                                        <span class="ci-name"><%# Eval("ProductName") %></span>
                                        <span class="ci-qty"><%# Eval("Quantity") %></span>
                                        <span class="ci-price">$<%# Eval("LineTotal", "{0:F2}") %></span>
                                        <asp:LinkButton ID="lnkRemove" runat="server" CommandName="remove"
                                            CommandArgument='<%# Container.ItemIndex %>' CssClass="ci-remove"
                                            Text="&#10005;" CausesValidation="false" />
                                    </div>
                                </ItemTemplate>
                            </asp:Repeater>
                        </asp:Panel>

                        <div class="est-box">
                            <span class="est-lbl">Estimated total</span>
                            <span class="est-val"><asp:Literal ID="litTotal" runat="server" Text="-" /></span>
                        </div>

                        <hr class="divider" />

                        <%-- Customer --%>
                        <div class="form-section-hdr">Customer</div>
                        <div class="form-2col">
                            <div class="form-row stacked">
                                <label>Name *</label>
                                <asp:TextBox ID="txtCustomerName" runat="server" />
                                <asp:RequiredFieldValidator runat="server" ControlToValidate="txtCustomerName"
                                    ValidationGroup="Step1" CssClass="err"
                                    ErrorMessage="Customer name is required." Display="Dynamic">*</asp:RequiredFieldValidator>
                            </div>
                            <div class="form-row stacked">
                                <label>Email *</label>
                                <asp:TextBox ID="txtEmail" runat="server" />
                                <asp:RequiredFieldValidator runat="server" ControlToValidate="txtEmail"
                                    ValidationGroup="Step1" CssClass="err"
                                    ErrorMessage="Email is required." Display="Dynamic">*</asp:RequiredFieldValidator>
                                <asp:RegularExpressionValidator runat="server" ControlToValidate="txtEmail"
                                    ValidationGroup="Step1" ValidationExpression="\S+@\S+\.\S+"
                                    CssClass="err" ErrorMessage="Invalid email address." Display="Dynamic">!</asp:RegularExpressionValidator>
                            </div>
                        </div>

                        <hr class="divider" />

                        <%-- Options --%>
                        <div class="form-section-hdr">Options</div>
                        <div class="form-row stacked">
                            <label>Priority</label>
                            <div class="priority-wrap">
                                <asp:RadioButtonList ID="rblPriority" runat="server"
                                    RepeatDirection="Horizontal" RepeatLayout="Flow">
                                    <asp:ListItem Text="Low"    Value="Low" />
                                    <asp:ListItem Text="Normal" Value="Normal" Selected="True" />
                                    <asp:ListItem Text="High"   Value="High" />
                                </asp:RadioButtonList>
                            </div>
                        </div>
                        <div class="form-row stacked">
                            <label>Add-ons</label>
                            <div class="extras-wrap">
                                <asp:CheckBoxList ID="cblExtras" runat="server" RepeatDirection="Horizontal" RepeatLayout="Flow">
                                    <asp:ListItem Text="Gift wrap"        Value="Gift wrap" />
                                    <asp:ListItem Text="Express delivery" Value="Express delivery" />
                                </asp:CheckBoxList>
                            </div>
                        </div>

                        <hr class="divider" />

                        <%-- Delivery Date --%>
                        <asp:UpdatePanel ID="upCalendar" runat="server" UpdateMode="Conditional">
                        <ContentTemplate>
                            <div class="form-section-hdr">Delivery Date</div>
                            <div style="margin-bottom:6px">
                                <asp:LinkButton ID="btnCalToday" runat="server" CssClass="btn btn-secondary"
                                    style="font-size:11px;padding:3px 10px" OnClick="btnCalToday_Click"
                                    CausesValidation="false">Today</asp:LinkButton>
                            </div>
                            <div class="calendar-wrap">
                                <asp:Calendar ID="calDelivery" runat="server"
                                    SelectionMode="Day"
                                    Font-Size="12px"
                                    TodayDayStyle-Font-Bold="true"
                                    SelectedDayStyle-BackColor="#1e3a5f"
                                    SelectedDayStyle-ForeColor="white"
                                    NextPrevStyle-ForeColor="#1e3a5f"
                                    TitleStyle-BackColor="#f0f2f5"
                                    TitleStyle-Font-Bold="true" />
                            </div>
                            <asp:CustomValidator ID="cvDate" runat="server"
                                ValidationGroup="Step1" CssClass="err"
                                ErrorMessage="Please select a delivery date (today or later)."
                                OnServerValidate="cvDate_ServerValidate" Display="Dynamic">!</asp:CustomValidator>
                        </ContentTemplate>
                        <Triggers>
                            <asp:AsyncPostBackTrigger ControlID="btnCalToday" EventName="Click" />
                        </Triggers>
                        </asp:UpdatePanel>

                        <div style="display:flex;justify-content:flex-end;margin-top:8px">
                            <asp:Button ID="btnNext" runat="server" Text="Next &rarr;"
                                CssClass="btn btn-primary" ValidationGroup="Step1" OnClick="btnNext_Click" />
                        </div>
                    </div>
                </asp:View>

                <%-- ===== Step 2: Review ===== --%>
                <asp:View ID="vStep2" runat="server">
                    <div>
                        <div class="alert alert-warn mb">Please review your order before confirming.</div>
                        <div class="review-section">
                            <div class="review-label">Products</div>
                            <asp:Repeater ID="rptRevItems" runat="server">
                                <ItemTemplate>
                                    <div class="review-item">
                                        <span class="ri-name"><%# Eval("ProductName") %></span>
                                        <span class="ri-qty">x <%# Eval("Quantity") %></span>
                                        <span class="ri-price">$<%# Eval("LineTotal", "{0:F2}") %></span>
                                    </div>
                                </ItemTemplate>
                            </asp:Repeater>
                            <div class="review-total">Total: <asp:Literal ID="litRevTotal" runat="server" /></div>
                        </div>
                        <table style="width:100%;border-collapse:collapse;font-size:13px;margin-top:12px">
                            <tr><td style="padding:6px 0;color:#7a7770;width:120px;border-bottom:1px solid #f5f4f1">Customer</td>  <td style="padding:6px 0;border-bottom:1px solid #f5f4f1"><asp:Literal ID="litRevCustomer" runat="server" /></td></tr>
                            <tr><td style="padding:6px 0;color:#7a7770;border-bottom:1px solid #f5f4f1">Email</td>     <td style="padding:6px 0;border-bottom:1px solid #f5f4f1"><asp:Literal ID="litRevEmail"    runat="server" /></td></tr>
                            <tr><td style="padding:6px 0;color:#7a7770;border-bottom:1px solid #f5f4f1">Priority</td>  <td style="padding:6px 0;border-bottom:1px solid #f5f4f1"><asp:Literal ID="litRevPriority" runat="server" /></td></tr>
                            <tr><td style="padding:6px 0;color:#7a7770;border-bottom:1px solid #f5f4f1">Delivery</td>  <td style="padding:6px 0;border-bottom:1px solid #f5f4f1"><asp:Literal ID="litRevDate"     runat="server" /></td></tr>
                            <tr><td style="padding:6px 0;color:#7a7770">Add-ons</td>   <td style="padding:6px 0"><asp:Literal ID="litRevExtras"   runat="server" /></td></tr>
                        </table>
                        <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:16px">
                            <asp:Button ID="btnBack" runat="server" Text="&larr; Back"
                                CssClass="btn btn-secondary" OnClick="btnBack_Click" CausesValidation="false" />
                            <asp:Button ID="btnConfirm" runat="server" Text="Place Order"
                                CssClass="btn btn-primary" OnClick="btnConfirm_Click" CausesValidation="false" />
                        </div>
                    </div>
                </asp:View>

                <%-- ===== Step 3: Confirmed ===== --%>
                <asp:View ID="vStep3" runat="server">
                    <div style="display:flex;flex-direction:column;align-items:center">
                        <div class="order-conf-box">
                            <div class="confirm-success">
                                <span class="confirm-check">&#10003;</span>
                                <div class="confirm-id"><asp:Literal ID="litOrderId" runat="server" /></div>
                                <div class="confirm-msg">Order placed successfully</div>
                            </div>
                            <div class="confirm-body">
                                <div class="confirm-section">
                                    <div class="confirm-label">Items</div>
                                    <asp:Repeater ID="rptConfirmItems" runat="server">
                                        <ItemTemplate>
                                            <div class="confirm-item">
                                                <span><%# Eval("ProductName") %></span>
                                                <span class="confirm-item-meta">x <%# Eval("Quantity") %> &middot; $<%# Eval("LineTotal", "{0:F2}") %></span>
                                            </div>
                                        </ItemTemplate>
                                    </asp:Repeater>
                                </div>
                                <div class="confirm-divider"></div>
                                <div class="confirm-section">
                                    <div class="confirm-label">Details</div>
                                    <div class="confirm-detail">
                                        <table style="width:100%;border-collapse:collapse;font-size:12px">
                                            <tr><td style="padding:4px 0;color:#9a9790">Total</td><td style="padding:4px 0;text-align:right;font-weight:600;font-family:'DM Mono',monospace">$<asp:Literal ID="litConfTotal" runat="server" /></td></tr>
                                            <tr><td style="padding:4px 0;color:#9a9790">Customer</td><td style="padding:4px 0;text-align:right"><asp:Literal ID="litConfCustomer" runat="server" /></td></tr>
                                            <tr><td style="padding:4px 0;color:#9a9790">Email</td><td style="padding:4px 0;text-align:right"><asp:Literal ID="litConfEmail" runat="server" /></td></tr>
                                            <tr><td style="padding:4px 0;color:#9a9790">Delivery</td><td style="padding:4px 0;text-align:right"><asp:Literal ID="litConfDate" runat="server" /></td></tr>
                                            <tr><td style="padding:4px 0;color:#9a9790">Priority</td><td style="padding:4px 0;text-align:right"><asp:Literal ID="litConfPriority" runat="server" /></td></tr>
                                            <tr id="trConfExtras" runat="server" visible="false"><td style="padding:4px 0;color:#9a9790">Add-ons</td><td style="padding:4px 0;text-align:right"><asp:Literal ID="litConfExtras" runat="server" /></td></tr>
                                        </table>
                                    </div>
                                </div>
                            </div>
                        </div>
                        <div style="margin-top:12px;width:100%;max-width:360px;display:flex;justify-content:flex-end">
                            <asp:Button ID="btnNewOrder" runat="server" Text="Place another order"
                                CssClass="btn btn-primary" OnClick="btnNewOrder_Click" CausesValidation="false" />
                        </div>
                    </div>
                </asp:View>

            </asp:MultiView>
        </div>

        <%-- Right: Order History in its own UpdatePanel --%>
        <asp:UpdatePanel ID="upHistory" runat="server" UpdateMode="Conditional">
        <ContentTemplate>
        <div class="card">
            <div class="ch" style="flex-wrap:wrap;gap:6px">
                <span class="ct">Order history</span>
            </div>
            <asp:Repeater ID="rptHistory" runat="server">
                <HeaderTemplate>
                    <table class="grid" style="width:100%">
                    <tr><th>#</th><th>Customer</th><th>Items</th><th>Total</th><th>Status</th></tr>
                </HeaderTemplate>
                <ItemTemplate>
                    <tr style="<%# (bool)Eval("IsDeleted") ? "opacity:0.45;text-decoration:line-through" : "" %>">
                        <td class="mono"><%# Eval("Id") %></td>
                        <td><%# Eval("CustomerName") %></td>
                        <td style="font-size:11px;line-height:1.5"><%# GetItemNames((int)Eval("Id")) %></td>
                        <td class="mono">$<%# Eval("Total", "{0:F2}") %></td>
                        <td><%# AppData.GetStatusBadge((bool)Eval("IsDeleted") ? "Deleted" : Eval("Status").ToString()) %></td>
                    </tr>
                </ItemTemplate>
                <FooterTemplate></table></FooterTemplate>
            </asp:Repeater>
            <div class="hist-pager">
                <asp:LinkButton ID="lnkHistPrev" runat="server" CssClass="btn btn-secondary" style="font-size:11px;padding:4px 10px"
                    OnClick="lnkHistPrev_Click" CausesValidation="false">&larr; Prev</asp:LinkButton>
                <asp:Label ID="lblHistPage" runat="server" CssClass="muted" style="font-size:11px;font-family:'DM Mono',monospace" />
                <asp:LinkButton ID="lnkHistNext" runat="server" CssClass="btn btn-secondary" style="font-size:11px;padding:4px 10px"
                    OnClick="lnkHistNext_Click" CausesValidation="false">Next &rarr;</asp:LinkButton>
            </div>
        </div>
        </ContentTemplate>
        </asp:UpdatePanel>

    </div>

    <%-- Manage Orders --%>
    <asp:UpdatePanel ID="upOrders" runat="server" UpdateMode="Conditional">
    <ContentTemplate>
    <div class="card">
        <div class="ch" style="flex-wrap:wrap;gap:8px">
            <span class="ct">Manage orders</span>
            <div class="filter-bar" style="margin-left:4px">
                <asp:DropDownList ID="ddlStatusFilter" runat="server" CssClass="fsel" AutoPostBack="true" OnSelectedIndexChanged="ddlStatusFilter_Changed">
                    <asp:ListItem Text="All statuses" Value="" />
                    <asp:ListItem Text="Pending"    Value="Pending" />
                    <asp:ListItem Text="Processing" Value="Processing" />
                    <asp:ListItem Text="Shipped"    Value="Shipped" />
                    <asp:ListItem Text="Delivered"  Value="Delivered" />
                </asp:DropDownList>
            </div>
            <div style="display:flex;align-items:center;gap:6px;font-size:12px;color:#7a7770;margin-left:auto">
                <span>Per page</span>
                <asp:DropDownList ID="ddlPageSizeO" runat="server" CssClass="fsel" AutoPostBack="true" OnSelectedIndexChanged="ddlPageSizeO_Changed">
                    <asp:ListItem Text="5"  Value="5"  Selected="True" />
                    <asp:ListItem Text="10" Value="10" />
                    <asp:ListItem Text="25" Value="25" />
                    <asp:ListItem Text="50" Value="50" />
                </asp:DropDownList>
            </div>
            <asp:Label ID="lblOrderCount" runat="server" CssClass="muted" style="font-family:'DM Mono',monospace" />
        </div>
        <asp:GridView ID="gvOrders" runat="server"
            AutoGenerateColumns="false"
            DataKeyNames="Id"
            AllowSorting="true"
            AllowPaging="true"
            PageSize="5"
            CssClass="grid"
            GridLines="None"
            Width="100%"
            OnSorting="gvOrders_Sorting"
            OnPageIndexChanging="gvOrders_PageIndexChanging"
            OnRowCreated="gvOrders_RowCreated"
            OnRowEditing="gvOrders_RowEditing"
            OnRowUpdating="gvOrders_RowUpdating"
            OnRowCancelingEdit="gvOrders_RowCancelingEdit"
            OnRowDeleting="gvOrders_RowDeleting"
            OnRowDataBound="gvOrders_RowDataBound">
            <Columns>
                <asp:BoundField DataField="Id" HeaderText="#" SortExpression="Id" ReadOnly="true" ItemStyle-Width="40px" />
                <asp:BoundField DataField="CustomerName" HeaderText="Customer" SortExpression="CustomerName" ReadOnly="true" />
                <asp:TemplateField HeaderText="Items" ItemStyle-Width="180px">
                    <ItemTemplate>
                        <div style="font-size:12px;line-height:1.5;color:#3a3936"><%# GetItemNames((int)Eval("Id")) %></div>
                    </ItemTemplate>
                </asp:TemplateField>
                <asp:BoundField DataField="Total" HeaderText="Total" DataFormatString="{0:F2}" ReadOnly="true" ItemStyle-Width="80px" HeaderStyle-HorizontalAlign="Right" ItemStyle-HorizontalAlign="Right" />
                <asp:TemplateField HeaderText="Status" SortExpression="Status">
                    <ItemTemplate><%# AppData.GetStatusBadge(Eval("Status").ToString()) %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:DropDownList ID="ddlEditStatus" runat="server">
                            <asp:ListItem>Pending</asp:ListItem>
                            <asp:ListItem>Processing</asp:ListItem>
                            <asp:ListItem>Shipped</asp:ListItem>
                            <asp:ListItem>Delivered</asp:ListItem>
                        </asp:DropDownList>
                    </EditItemTemplate>
                </asp:TemplateField>
                <asp:TemplateField HeaderText="Priority" SortExpression="Priority">
                    <ItemTemplate><%# Eval("Priority") %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:DropDownList ID="ddlEditPriority" runat="server">
                            <asp:ListItem>Low</asp:ListItem>
                            <asp:ListItem>Normal</asp:ListItem>
                            <asp:ListItem>High</asp:ListItem>
                        </asp:DropDownList>
                    </EditItemTemplate>
                </asp:TemplateField>
                <asp:BoundField DataField="OrderDate" HeaderText="Date" DataFormatString="{0:d}" ReadOnly="true" SortExpression="OrderDate" ItemStyle-Width="90px" />
                <asp:CommandField ShowEditButton="true" ShowDeleteButton="true" ButtonType="Link"
                    EditText="Edit" UpdateText="Save" CancelText="Cancel" DeleteText="Delete"
                    ControlStyle-CssClass="action-link" />
            </Columns>
            <EmptyDataTemplate>
                <div style="padding:20px;text-align:center;color:#6b7280">No orders yet.</div>
            </EmptyDataTemplate>
        </asp:GridView>
        <asp:HiddenField ID="hdnDeleteOrderId" runat="server" Value="" />
        <asp:Button ID="btnConfirmDelete" runat="server" Text="" style="display:none" OnClick="btnConfirmDelete_Click" />
    </div>
    </ContentTemplate>
    </asp:UpdatePanel>

    <script type="text/javascript">
    var _comboActive = -1;

    function initCombo(id) {
        var wrap = document.getElementById(id);
        if (!wrap) return;
        var input = wrap.querySelector('.combo-input');
        var sel = wrap.querySelector('select');
        var list = wrap.querySelector('.combo-list');
        if (!input || !sel || !list) return;

        sel._allOpts = [];
        for (var i = 0; i < sel.options.length; i++) {
            var v = sel.options[i].value;
            var t = sel.options[i].text;
            if (v === '' || v === t || t.indexOf('--') === 0) continue;
            sel._allOpts.push({ text: t, value: v });
        }

        input.value = '';
        _comboActive = -1;

        input.oninput = function () { filterCombo(id); };
        input.onfocus = function () { openCombo(id); };
        input.onblur = function () { setTimeout(function () { closeCombo(id); }, 200); };
        input.onkeydown = function (e) { comboKeydown(e, id); };

        wrap.querySelector('.combo-arrow').onclick = function () { toggleCombo(id); };
    }

    function populateCombo(id) {
        var wrap = document.getElementById(id);
        var sel = wrap.querySelector('select');
        var list = wrap.querySelector('.combo-list');
        var input = wrap.querySelector('.combo-input');
        if (!sel || !sel._allOpts || !list) return;

        var term = (input ? input.value : '').toLowerCase();
        list.innerHTML = '';
        _comboActive = -1;

        for (var i = 0; i < sel._allOpts.length; i++) {
            if (sel._allOpts[i].value === '') continue;
            if (sel._allOpts[i].text.toLowerCase().indexOf(term) !== -1) {
                var div = document.createElement('div');
                div.className = 'combo-item';
                div.textContent = sel._allOpts[i].text;
                div.setAttribute('data-value', sel._allOpts[i].value);
                (function (val, txt) {
                    div.onmousedown = function (e) {
                        e.preventDefault();
                        selectComboItem(id, val, txt);
                    };
                })(sel._allOpts[i].value, sel._allOpts[i].text);
                list.appendChild(div);
            }
        }

        if (list.children.length === 0) {
            var empty = document.createElement('div');
            empty.className = 'combo-empty';
            empty.textContent = 'No products found';
            list.appendChild(empty);
        }
    }

    function openCombo(id) {
        var wrap = document.getElementById(id);
        if (!wrap) return;
        populateCombo(id);
        wrap.querySelector('.combo-list').classList.add('open');
        _comboActive = -1;
    }

    function closeCombo(id) {
        var wrap = document.getElementById(id);
        if (!wrap) return;
        var list = wrap.querySelector('.combo-list');
        if (list) list.classList.remove('open');
        _comboActive = -1;
    }

    function toggleCombo(id) {
        var wrap = document.getElementById(id);
        var list = wrap.querySelector('.combo-list');
        if (list.classList.contains('open')) closeCombo(id);
        else { wrap.querySelector('.combo-input').focus(); }
    }

    function filterCombo(id) {
        var wrap = document.getElementById(id);
        var list = wrap.querySelector('.combo-list');
        populateCombo(id);
        if (list && !list.classList.contains('open')) list.classList.add('open');
    }

    function selectComboItem(id, value, text) {
        var wrap = document.getElementById(id);
        var input = wrap.querySelector('.combo-input');
        var sel = wrap.querySelector('select');

        input.value = text;
        closeCombo(id);

        for (var i = 0; i < sel.options.length; i++) {
            if (sel.options[i].value === value) {
                sel.selectedIndex = i;
                return;
            }
        }
    }

    function comboKeydown(e, id) {
        var wrap = document.getElementById(id);
        var list = wrap.querySelector('.combo-list');
        var items = list.querySelectorAll('.combo-item');

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            if (!list.classList.contains('open')) { openCombo(id); return; }
            _comboActive = Math.min(_comboActive + 1, items.length - 1);
            setHighlight(items);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            _comboActive = Math.max(_comboActive - 1, 0);
            setHighlight(items);
        } else if (e.key === 'Enter' && _comboActive >= 0 && items[_comboActive]) {
            e.preventDefault();
            var val = items[_comboActive].getAttribute('data-value');
            var txt = items[_comboActive].textContent;
            selectComboItem(id, val, txt);
        } else if (e.key === 'Escape') {
            closeCombo(id);
            wrap.querySelector('.combo-input').blur();
        }
    }

    function setHighlight(items) {
        for (var i = 0; i < items.length; i++) {
            if (i === _comboActive) {
                items[i].classList.add('active');
                items[i].scrollIntoView({ block: 'nearest' });
            } else {
                items[i].classList.remove('active');
            }
        }
    }

    initCombo('productCombo');

    if (typeof Sys !== 'undefined' && typeof Sys.WebForms !== 'undefined' &&
        typeof Sys.WebForms.PageRequestManager !== 'undefined') {
        Sys.WebForms.PageRequestManager.getInstance().add_endRequest(function () {
            initCombo('productCombo');
        });
    }
    </script>

</asp:Content>
