<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="AddProductPanel.ascx.cs" Inherits="CoreWebForms.AddProductPanelControl" %>
<div style="margin-bottom:14px;display:flex;justify-content:flex-end">
    <asp:LinkButton ID="lnkAddProduct" runat="server" CssClass="btn btn-primary" OnClick="lnkAddProduct_Click">+ Add Product</asp:LinkButton>
</div>
<asp:Panel ID="pnlAdd" runat="server" Visible="false" CssClass="card card-pad" style="border-color:#1e3a5f">
    <h3 style="font-size:15px;font-weight:600;letter-spacing:-0.3px;margin-bottom:14px">Add New Product</h3>
    <asp:ValidationSummary ID="vsSummary" runat="server" ValidationGroup="AddProduct"
        CssClass="validation-summary" ShowMessageBox="false" ShowSummary="true" />
    <asp:Label ID="lblAddResult" runat="server" CssClass="alert alert-success mb" Visible="false" />
    <div class="form-row">
        <label>Name *</label>
                        <asp:TextBox ID="txtNewName" runat="server" Width="250px" MaxLength="200" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="txtNewName"
            ValidationGroup="AddProduct" CssClass="err"
            ErrorMessage="Product name is required." Display="Dynamic">*</asp:RequiredFieldValidator>
    </div>
    <div class="form-row">
        <label>Category *</label>
        <asp:DropDownList ID="ddlNewCategory" runat="server" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="ddlNewCategory"
            ValidationGroup="AddProduct" InitialValue=""
            CssClass="err" ErrorMessage="Please select a category." Display="Dynamic">*</asp:RequiredFieldValidator>
    </div>
    <div class="form-row">
        <label>Price *</label>
                        <asp:TextBox ID="txtNewPrice" runat="server" Width="100px" placeholder="0.00" MaxLength="10" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="txtNewPrice"
            ValidationGroup="AddProduct" CssClass="err"
            ErrorMessage="Price is required." Display="Dynamic">*</asp:RequiredFieldValidator>
        <asp:RangeValidator runat="server" ControlToValidate="txtNewPrice"
            ValidationGroup="AddProduct" MinimumValue="0.01" MaximumValue="99999" Type="Double"
            CssClass="err" ErrorMessage="Price must be between 0.01 and 99999." Display="Dynamic">Range err</asp:RangeValidator>
    </div>
    <div class="form-row">
        <label>Stock *</label>
                        <asp:TextBox ID="txtNewStock" runat="server" Width="80px" placeholder="0" MaxLength="7" />
        <asp:RequiredFieldValidator runat="server" ControlToValidate="txtNewStock"
            ValidationGroup="AddProduct" CssClass="err"
            ErrorMessage="Stock quantity is required." Display="Dynamic">*</asp:RequiredFieldValidator>
        <asp:RangeValidator runat="server" ControlToValidate="txtNewStock"
            ValidationGroup="AddProduct" MinimumValue="0" MaximumValue="999999" Type="Integer"
            CssClass="err" ErrorMessage="Stock must be between 0 and 999999." Display="Dynamic">Range err</asp:RangeValidator>
    </div>
    <div class="form-row">
        <label>Active</label>
        <asp:CheckBox ID="chkNewActive" runat="server" Checked="true" />
    </div>
    <div class="form-row">
        <asp:Button ID="btnSaveNew" runat="server" Text="Save Product" CssClass="btn btn-primary"
            ValidationGroup="AddProduct" OnClick="btnSaveNew_Click" />
        <asp:Button ID="btnCancelNew" runat="server" Text="Cancel" CssClass="btn btn-secondary"
            OnClick="btnCancelNew_Click" CausesValidation="false" />
    </div>
</asp:Panel>
