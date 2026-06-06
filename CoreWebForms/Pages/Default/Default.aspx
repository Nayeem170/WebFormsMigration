<%@ Page Title="Dashboard" Language="C#" MasterPageFile="~/Layout/Site.Master" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="CoreWebForms.DefaultPage" %>
<%@ Register TagPrefix="uc" TagName="PageHeader"  Src="~/Pages/Shared/PageHeader/PageHeader.ascx"  %>
<%@ Register TagPrefix="uc" TagName="OrdersTable" Src="~/Pages/Shared/OrdersTable/OrdersTable.ascx" %>
<%@ Register TagPrefix="uc" TagName="StatCards"    Src="StatCards/StatCards.ascx"         %>
<%@ Register TagPrefix="uc" TagName="CategoryExpand" Src="CategoryExpand/CategoryExpand.ascx" %>
<%@ Register TagPrefix="uc" TagName="OutOfStock"     Src="OutOfStock/OutOfStock.ascx"       %>

<asp:Content ID="HeadContent" ContentPlaceHolderID="HeadContent" runat="server">
    <link href="<%= ResolveUrl("~/Pages/Default/dashboard.css") %>" rel="stylesheet" />
</asp:Content>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
    <uc:PageHeader runat="server" Title="Dashboard" Subtitle="Inventory overview" />

    <uc:StatCards ID="statCards" runat="server" />

    <div class="two-col">
        <div class="card">
            <div class="ch">
                <span class="ct">Recent Orders</span>
                <span style="font-size:11px;color:#9a9790">last 6</span>
            </div>
            <uc:OrdersTable ID="ordersTable" runat="server" />
        </div>

        <div>
            <uc:CategoryExpand ID="catExpand" runat="server" />

            <uc:OutOfStock ID="outOfStock" runat="server" />
        </div>
    </div>

</asp:Content>
