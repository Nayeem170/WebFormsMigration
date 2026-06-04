<%@ Page Title="Error" Language="C#" MasterPageFile="~/Layout/Site.Master" AutoEventWireup="true" CodeBehind="ErrorPage.aspx.cs" Inherits="LegacyWebForms.ErrorPage" %>
<asp:Content ID="Content1" ContentPlaceHolderID="MainContent" runat="server">
    <div class="error-page">
        <h1>Something went wrong</h1>
        <p class="error-sub">An unexpected error occurred. Please try again or return to the dashboard.</p>
        <a href="~/Pages/Default/Default.aspx" runat="server" class="btn btn-primary">Back to Dashboard</a>
    </div>
</asp:Content>
