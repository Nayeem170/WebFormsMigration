<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="OutOfStock.ascx.cs" Inherits="CoreWebForms.OutOfStockControl" %>
<%@ Import Namespace="CoreWebForms" %>
<div class="card">
    <div class="ch">
        <span class="ct">Out of Stock</span>
        <asp:Literal ID="litOosCount" runat="server" />
    </div>
    <asp:Repeater ID="rptOutOfStock" runat="server">
        <ItemTemplate>
            <div class="oos-row">
                <span class="oos-dot"></span>
                <%#: Eval("Name") %>
                <span style="margin-left:auto"><span class="badge b-oos">OOS</span></span>
            </div>
        </ItemTemplate>
    </asp:Repeater>
    <asp:Panel ID="pnlNoOos" runat="server">
        <div class="oos-row" style="color:#9a9790">All products in stock</div>
    </asp:Panel>
</div>
