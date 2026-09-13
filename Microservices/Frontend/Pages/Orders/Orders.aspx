<%@ Page Title="Orders" Language="C#" MasterPageFile="~/Layout/Site.Master" AutoEventWireup="true" CodeBehind="Orders.aspx.cs" Inherits="CoreWebForms.OrdersPage" %>
<%@ Register TagPrefix="uc" TagName="PageHeader"    Src="~/Pages/Shared/PageHeader/PageHeader.ascx"  %>
<%@ Register TagPrefix="uc" TagName="OrderWizard"   Src="OrderWizard/OrderWizard.ascx"  %>
<%@ Register TagPrefix="uc" TagName="OrderHistory"   Src="OrderHistory/OrderHistory.ascx"  %>
<%@ Register TagPrefix="uc" TagName="OrdersManage"   Src="OrdersManage/OrdersManage.ascx"  %>

<asp:Content ID="HeadContent" ContentPlaceHolderID="HeadContent" runat="server">
    <link href="<%= ResolveUrl("~/Pages/Orders/orders.css") %>" rel="stylesheet" />
</asp:Content>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">
    <uc:PageHeader runat="server" Title="Orders" Subtitle="Place and manage customer orders" />

    <div class="orders-layout">
        <uc:OrderWizard ID="orderWizard" runat="server" OnOrderPlaced="orderWizard_OrderPlaced" />
        <uc:OrderHistory ID="orderHistory" runat="server" />
    </div>

    <uc:OrdersManage ID="ordersManage" runat="server" OnOrderDeleted="ordersManage_OrderDeleted" />
</asp:Content>
