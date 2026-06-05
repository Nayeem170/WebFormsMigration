<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="StatCards.ascx.cs" Inherits="LegacyWebForms.StatCardsControl" %>
<div class="stats">
        <a href="~/Pages/Products/Products.aspx" runat="server" class="sc">
        <div class="sc-label">Total Products</div>
        <div class="sc-val"><asp:Literal ID="litTotalProducts" runat="server" /></div>
        <div class="sc-hint">view all &rarr;</div>
    </a>
        <a href="~/Pages/Products/Products.aspx" runat="server" class="sc warn-border">
        <div class="sc-label">Low Stock (&le;5)</div>
        <div class="sc-val amber"><asp:Literal ID="litLowStock" runat="server" /></div>
        <div class="sc-hint amber">needs attention &rarr;</div>
    </a>
        <a href="~/Pages/Orders/Orders.aspx" runat="server" class="sc">
        <div class="sc-label">Total Orders</div>
        <div class="sc-val"><asp:Literal ID="litTotalOrders" runat="server" /></div>
        <div class="sc-hint">view all &rarr;</div>
    </a>
        <a href="~/Pages/Orders/Orders.aspx" runat="server" class="sc red-border">
        <div class="sc-label">Pending Orders</div>
        <div class="sc-val red"><asp:Literal ID="litPending" runat="server" /></div>
        <div class="sc-hint red">action needed &rarr;</div>
    </a>
</div>
