<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="ProductSummary.ascx.cs" Inherits="CoreWebForms.ProductSummaryControl" %>
<div class="stats">
    <div class="sc">
        <div class="sc-label">Total Products</div>
        <div class="sc-val"><asp:Literal ID="litProdTotal" runat="server" /></div>
    </div>
    <div class="sc">
        <div class="sc-label">Active</div>
        <div class="sc-val"><asp:Literal ID="litProdActive" runat="server" /></div>
    </div>
    <div class="sc warn-border">
        <div class="sc-label">Low Stock</div>
        <div class="sc-val amber"><asp:Literal ID="litProdLow" runat="server" /></div>
        <div class="sc-hint amber">1&ndash;5 units</div>
    </div>
    <div class="sc red-border">
        <div class="sc-label">Out of Stock</div>
        <div class="sc-val red"><asp:Literal ID="litProdOos" runat="server" /></div>
        <div class="sc-hint red">0 units</div>
    </div>
</div>
