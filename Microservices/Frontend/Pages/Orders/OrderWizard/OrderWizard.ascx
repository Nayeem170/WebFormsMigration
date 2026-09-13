<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="OrderWizard.ascx.cs" Inherits="CoreWebForms.OrderWizardControl" %>
<script src='<%= ResolveUrl("./combo.js") %>' type="text/javascript"></script>

<div class="card card-pad">
    <div class="wizard-tabs">
        <asp:Panel ID="pnlStep1" runat="server" CssClass="wt active">1. Order info</asp:Panel>
        <asp:Panel ID="pnlStep2" runat="server" CssClass="wt">2. Review</asp:Panel>
        <asp:Panel ID="pnlStep3" runat="server" CssClass="wt">3. Confirmed</asp:Panel>
    </div>

    <asp:MultiView ID="mvOrder" runat="server" ActiveViewIndex="0">

        <asp:View ID="vStep1" runat="server">
            <div>

                <asp:ValidationSummary ID="vs1" runat="server" ValidationGroup="Step1"
                    CssClass="validation-summary" ShowSummary="true" ShowMessageBox="false" />

                <div class="form-section-hdr">Products</div>
                <div class="add-product-row">
                    <div class="combo-wrap" id="productCombo" runat="server">
                        <div class="combo-trigger">
                            <input type="text" id="txtProductInput" class="combo-input"
                                   placeholder="Select or search product..." autocomplete="off" />
                            <span class="combo-arrow">&#9662;</span>
                        </div>
                        <div class="combo-list"></div>
                        <asp:DropDownList ID="ddlProduct" runat="server" style="display:none" />
                    </div>
                    <asp:TextBox ID="txtQty" runat="server" Width="60px" Text="1"
                        CssClass="qty-input" MaxLength="6" />
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
                                <span class="ci-name"><%#: Eval("ProductName") %></span>
                                <span class="ci-qty"><%# Eval("Quantity") %></span>
                                <span class="ci-price">$<%# Eval("LineTotal", "{0:F2}") %></span>
                                <asp:LinkButton ID="lnkRemove" runat="server" CommandName="remove"
                                    CommandArgument='<%# Eval("ProductId") %>' CssClass="ci-remove"
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

                <div class="form-section-hdr">Customer</div>
                <div class="form-2col">
                    <div class="form-row stacked">
                        <label>Name *</label>
                        <asp:TextBox ID="txtCustomerName" runat="server" MaxLength="200" />
                        <asp:RequiredFieldValidator runat="server" ControlToValidate="txtCustomerName"
                            ValidationGroup="Step1" CssClass="err"
                            ErrorMessage="Customer name is required." Display="Dynamic">*</asp:RequiredFieldValidator>
                    </div>
                    <div class="form-row stacked">
                        <label>Email *</label>
                        <asp:TextBox ID="txtEmail" runat="server" MaxLength="200" />
                        <asp:RequiredFieldValidator runat="server" ControlToValidate="txtEmail"
                            ValidationGroup="Step1" CssClass="err"
                            ErrorMessage="Email is required." Display="Dynamic">*</asp:RequiredFieldValidator>
                        <asp:RegularExpressionValidator runat="server" ControlToValidate="txtEmail"
                            ValidationGroup="Step1"                     ValidationExpression="^[^\s@]+@[^\s@]+\.[^\s@]+$"
                            CssClass="err" ErrorMessage="Invalid email address." Display="Dynamic">!</asp:RegularExpressionValidator>
                    </div>
                </div>

                <hr class="divider" />

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
                <asp:Label ID="lblDateError" runat="server" CssClass="err"
                    Text="Please select a delivery date (today or later)." Visible="false" />

                <div style="display:flex;justify-content:flex-end;margin-top:8px">
                    <asp:Button ID="btnNext" runat="server" Text="Next &rarr;"
                        CssClass="btn btn-primary" ValidationGroup="Step1" OnClick="btnNext_Click" />
                </div>
            </div>
        </asp:View>

        <asp:View ID="vStep2" runat="server">
            <div>
                <div class="alert alert-warn mb">Please review your order before confirming.</div>
                <div class="review-section">
                    <div class="review-label">Products</div>
                    <asp:Repeater ID="rptRevItems" runat="server">
                        <ItemTemplate>
                            <div class="review-item">
                                <span class="ri-name"><%#: Eval("ProductName") %></span>
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

        <asp:View ID="vStep3" runat="server">
            <div style="display:flex;flex-direction:column;align-items:center">
                <div class="order-conf-box">
                    <div class="confirm-success">
                        <span class="confirm-check">&#10003;</span>
                        <div class="confirm-id"><asp:Literal ID="litOrderId" runat="server" /></div>
                        <div class="order-conf-msg">Order placed successfully</div>
                    </div>
                    <div class="confirm-body">
                        <div class="confirm-section">
                            <div class="confirm-label">Items</div>
                            <asp:Repeater ID="rptConfirmItems" runat="server">
                                <ItemTemplate>
                                    <div class="confirm-item">
                                        <span><%#: Eval("ProductName") %></span>
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

<script type="text/javascript">
initCombo('<%= productCombo.ClientID %>');
if (typeof Sys !== 'undefined' && typeof Sys.WebForms !== 'undefined' &&
    typeof Sys.WebForms.PageRequestManager !== 'undefined' && !window._comboEndReq) {
    window._comboEndReq = true;
    Sys.WebForms.PageRequestManager.getInstance().add_endRequest(function () {
        var w = document.getElementById('<%= productCombo.ClientID %>');
        var inp = w ? w.querySelector('.combo-input') : null;
        var saved = inp ? inp.value : '';
        initCombo('<%= productCombo.ClientID %>');
        if (inp && saved) inp.value = saved;
    });
}
</script>
