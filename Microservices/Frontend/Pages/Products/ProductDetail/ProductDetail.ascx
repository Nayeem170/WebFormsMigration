<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="ProductDetail.ascx.cs" Inherits="CoreWebForms.ProductDetailControl" %>
<asp:Panel ID="pnlDetail" runat="server" Visible="false" CssClass="card">
    <div class="ch"><span class="ct">Product Details</span>
        <asp:LinkButton ID="lnkCloseDetail" runat="server" CssClass="action-link" OnClick="lnkCloseDetail_Click">Close</asp:LinkButton>
    </div>
    <div class="detail-grid">
        <div class="detail-row"><span class="detail-key">ID</span><span class="detail-val mono"><asp:Literal ID="detId" runat="server" /></span></div>
        <div class="detail-row"><span class="detail-key">Name</span><span class="detail-val"><asp:Literal ID="detName" runat="server" /></span></div>
        <div class="detail-row"><span class="detail-key">Category</span><span class="detail-val"><asp:Literal ID="detCat" runat="server" /></span></div>
        <div class="detail-row"><span class="detail-key">Price</span><span class="detail-val mono"><asp:Literal ID="detPrice" runat="server" /></span></div>
        <div class="detail-row"><span class="detail-key">Stock</span><span class="detail-val"><asp:Literal ID="detStock" runat="server" /></span></div>
        <div class="detail-row"><span class="detail-key">Status</span><span class="detail-val"><asp:Literal ID="detStatus" runat="server" /></span></div>
        <div class="detail-row"><span class="detail-key">Added</span><span class="detail-val"><asp:Literal ID="detAdded" runat="server" /></span></div>
    </div>
</asp:Panel>
