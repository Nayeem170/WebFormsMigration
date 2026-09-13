<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="CategoryExpand.ascx.cs" Inherits="CoreWebForms.CategoryExpandControl" %>
<div class="card" runat="server" id="cardCategories">
    <div class="ch"><span class="ct">Products by Category</span></div>
    <asp:Repeater ID="rptCatExpand" runat="server" OnItemCommand="rptCatExpand_ItemCommand">
        <ItemTemplate>
            <div class="cat-row">
                <asp:LinkButton ID="lnkCat" runat="server" CommandName="toggle" CommandArgument='<%# Eval("Category") %>' Text='<%# Eval("Category") %>'></asp:LinkButton>
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
<asp:PlaceHolder ID="phUnavailable" runat="server" Visible="false">
    <div class="card" style="padding:12px;color:#b3372f"><asp:Literal ID="litUnavailable" runat="server" /></div>
</asp:PlaceHolder>
