<%@ Page Title="Dashboard" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="LegacyWebForms.DefaultPage" %>
<%@ Import Namespace="LegacyWebForms" %>

<asp:Content ID="HeadContent" ContentPlaceHolderID="HeadContent" runat="server">
</asp:Content>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
    <div class="ph">
        <div>
            <div class="pt">Dashboard</div>
            <div class="ps">Inventory overview</div>
        </div>
    </div>

    <%-- Stat cards using Literal --%>
    <div class="stats">
        <a href="~/Products.aspx" runat="server" class="sc">
            <div class="sc-label">Total Products</div>
            <div class="sc-val"><asp:Literal ID="litTotalProducts" runat="server" /></div>
            <div class="sc-hint">view all &rarr;</div>
        </a>
        <a href="~/Products.aspx" runat="server" class="sc warn-border">
            <div class="sc-label">Low Stock (&le;5)</div>
            <div class="sc-val amber"><asp:Literal ID="litLowStock" runat="server" /></div>
            <div class="sc-hint amber">needs attention &rarr;</div>
        </a>
        <a href="~/Orders.aspx" runat="server" class="sc">
            <div class="sc-label">Total Orders</div>
            <div class="sc-val"><asp:Literal ID="litTotalOrders" runat="server" /></div>
            <div class="sc-hint">view all &rarr;</div>
        </a>
        <a href="~/Orders.aspx" runat="server" class="sc red-border">
            <div class="sc-label">Pending Orders</div>
            <div class="sc-val red"><asp:Literal ID="litPending" runat="server" /></div>
            <div class="sc-hint red">action needed &rarr;</div>
        </a>
    </div>

    <div class="two-col">
        <%-- Recent orders using Repeater with AlternatingItemTemplate --%>
        <div class="card">
            <div class="ch">
                <span class="ct">Recent Orders</span>
                <span style="font-size:11px;color:#9a9790">last 6</span>
            </div>
            <asp:Repeater ID="rptOrders" runat="server">
                <HeaderTemplate>
                    <table class="grid" style="width:100%">
                    <tr><th>#</th><th>Customer</th><th>Items</th><th>Total</th><th>Status</th></tr>
                </HeaderTemplate>
                <ItemTemplate>
                    <tr>
                        <td class="mono"><%# Eval("Id") %></td>
                        <td><%# Eval("CustomerName") %></td>
                        <td style="font-size:11px;line-height:1.5"><%# GetOrderItemsSummary((int)Eval("Id")) %></td>
                        <td class="mono">$<%# Eval("Total", "{0:F2}") %></td>
                        <td><%# AppData.GetStatusBadge(Eval("Status").ToString()) %></td>
                    </tr>
                </ItemTemplate>
                <AlternatingItemTemplate>
                    <tr style="background:#faf9f6">
                        <td class="mono"><%# Eval("Id") %></td>
                        <td><%# Eval("CustomerName") %></td>
                        <td style="font-size:11px;line-height:1.5"><%# GetOrderItemsSummary((int)Eval("Id")) %></td>
                        <td class="mono">$<%# Eval("Total", "{0:F2}") %></td>
                        <td><%# AppData.GetStatusBadge(Eval("Status").ToString()) %></td>
                    </tr>
                </AlternatingItemTemplate>
                <FooterTemplate></table></FooterTemplate>
            </asp:Repeater>
        </div>

        <div>
            <%-- Products by Category: expandable Repeater --%>
            <div class="card">
                <div class="ch"><span class="ct">Products by Category</span></div>
                <asp:Repeater ID="rptCatExpand" runat="server" OnItemCommand="rptCatExpand_ItemCommand">
                    <ItemTemplate>
                        <div class="cat-row">
                            <asp:LinkButton runat="server" CommandName="toggle"
                                CommandArgument='<%# Eval("Category") %>'
                                CssClass="cat-name" style="text-decoration:none;background:none;border:none;cursor:pointer">
                                <%# Eval("Category") %>
                            </asp:LinkButton>
                            <div class="cat-right">
                                <span class="cat-pill"><%# Eval("Count") %></span>
                                <span class="cat-chev<%# (bool)Eval("Expanded") ? " open" : "" %>">&rsaquo;</span>
                            </div>
                        </div>
                        <asp:Panel runat="server" Visible='<%# (bool)Eval("Expanded") %>'>
                            <div class="cat-sub">
                                <asp:Literal runat="server" Text='<%# GetProductList(Eval("ProductNames")) %>' />
                            </div>
                        </asp:Panel>
                    </ItemTemplate>
                </asp:Repeater>
            </div>

            <%-- Out of stock: Repeater with oos-row pattern --%>
            <div class="card">
                <div class="ch">
                    <span class="ct">Out of Stock</span>
                    <asp:Literal ID="litOosCount" runat="server" />
                </div>
                <asp:Repeater ID="rptOutOfStock" runat="server">
                    <ItemTemplate>
                        <div class="oos-row">
                            <span class="oos-dot"></span>
                            <%# Eval("Name") %>
                            <span style="margin-left:auto"><span class="badge b-oos">OOS</span></span>
                        </div>
                    </ItemTemplate>
                </asp:Repeater>
                <asp:Panel ID="pnlNoOos" runat="server">
                    <div class="oos-row" style="color:#9a9790">All products in stock</div>
                </asp:Panel>
            </div>
        </div>
    </div>

</asp:Content>
