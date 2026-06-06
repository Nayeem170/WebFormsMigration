<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="OrdersTable.ascx.cs" Inherits="CoreWebForms.OrdersTableControl" %>
<%@ Import Namespace="CoreWebForms" %>
<asp:Repeater ID="rptTable" runat="server">
    <HeaderTemplate>
        <table class="grid" style="width:100%">
        <tr><th>#</th><th>Customer</th><th>Items</th><th>Total</th><th>Status</th></tr>
    </HeaderTemplate>
    <ItemTemplate>
        <tr style="<%# RowStyle(Eval("IsDeleted")) %>">
            <td class="mono"><%#: Eval("Id") %></td>
            <td><%#: Eval("CustomerName") %></td>
            <td style="font-size:11px;line-height:1.5"><%# ItemNames(Convert.ToInt32(Eval("Id"))) %></td>
            <td class="mono">$<%#: Eval("Total", "{0:F2}") %></td>
            <td><%# StatusBadge(Eval("IsDeleted"), Eval("Status")) %></td>
        </tr>
    </ItemTemplate>
    <FooterTemplate></table></FooterTemplate>
</asp:Repeater>
