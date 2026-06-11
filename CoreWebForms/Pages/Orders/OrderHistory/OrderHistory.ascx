<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="OrderHistory.ascx.cs" Inherits="CoreWebForms.OrderHistoryControl" %>
<%@ Register TagPrefix="uc" TagName="OrdersTable" Src="~/Pages/Shared/OrdersTable/OrdersTable.ascx" %>
<asp:UpdatePanel ID="upHistory" runat="server" UpdateMode="Conditional">
<ContentTemplate>
<div class="card">
    <div class="ch" style="flex-wrap:wrap;gap:6px">
        <span class="ct">Order history</span>
    </div>
    <uc:OrdersTable ID="ordersTable" runat="server" ShowDeleted="True" />
    <div class="hist-pager">
        <asp:LinkButton ID="lnkHistPrev" runat="server" CssClass="btn btn-secondary" style="font-size:11px;padding:4px 10px"
            OnClick="lnkHistPrev_Click" CausesValidation="false">&larr; Prev</asp:LinkButton>
        <asp:Label ID="lblHistPage" runat="server" CssClass="muted" style="font-size:11px;font-family:'DM Mono',monospace" />
        <asp:LinkButton ID="lnkHistNext" runat="server" CssClass="btn btn-secondary" style="font-size:11px;padding:4px 10px"
            OnClick="lnkHistNext_Click" CausesValidation="false">Next &rarr;</asp:LinkButton>
    </div>
</div>
</ContentTemplate>
</asp:UpdatePanel>
