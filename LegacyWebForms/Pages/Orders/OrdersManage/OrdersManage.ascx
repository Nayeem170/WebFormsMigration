<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="OrdersManage.ascx.cs" Inherits="LegacyWebForms.OrdersManageControl" %>
<%@ Import Namespace="LegacyWebForms" %>
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
    <asp:Label ID="lblOrderError" runat="server" CssClass="alert alert-warn mb" Visible="false" />
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
            <asp:BoundField DataField="Total" HeaderText="Total" DataFormatString="{0:F2}" ReadOnly="true" SortExpression="Total" ItemStyle-Width="80px" HeaderStyle-HorizontalAlign="Right" ItemStyle-HorizontalAlign="Right" />
            <asp:TemplateField HeaderText="Status" SortExpression="Status">
                <ItemTemplate><%# StatusBadge(Eval("Status")) %></ItemTemplate>
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
                <ItemTemplate><%#: Eval("Priority") %></ItemTemplate>
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
