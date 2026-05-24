<%@ Page Title="Products" Language="C#" MasterPageFile="~/Site.Master" AutoEventWireup="true" CodeBehind="Products.aspx.cs" Inherits="LegacyWebForms.ProductsPage" %>

<asp:Content ID="HeadContent" ContentPlaceHolderID="HeadContent" runat="server">
</asp:Content>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <div class="ph">
        <div>
            <div class="pt">Products</div>
            <div class="ps">Browse and manage inventory</div>
        </div>
    </div>

    <%-- Product Summary --%>
    <div class="stats">
        <div class="sc">
            <div class="sc-label">Total Products</div>
            <div class="sc-val"><asp:Literal ID="litProdTotal"  runat="server" /></div>
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

    <%-- Add Product toggle + panel --%>
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
            <asp:TextBox ID="txtNewName" runat="server" Width="250px" />
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
            <asp:TextBox ID="txtNewPrice" runat="server" Width="100px" placeholder="0.00" />
            <asp:RequiredFieldValidator runat="server" ControlToValidate="txtNewPrice"
                ValidationGroup="AddProduct" CssClass="err"
                ErrorMessage="Price is required." Display="Dynamic">*</asp:RequiredFieldValidator>
            <asp:RangeValidator runat="server" ControlToValidate="txtNewPrice"
                ValidationGroup="AddProduct" MinimumValue="0.01" MaximumValue="99999" Type="Double"
                CssClass="err" ErrorMessage="Price must be between 0.01 and 99999." Display="Dynamic">Range err</asp:RangeValidator>
        </div>
        <div class="form-row">
            <label>Stock *</label>
            <asp:TextBox ID="txtNewStock" runat="server" Width="80px" placeholder="0" />
            <asp:RequiredFieldValidator runat="server" ControlToValidate="txtNewStock"
                ValidationGroup="AddProduct" CssClass="err"
                ErrorMessage="Stock quantity is required." Display="Dynamic">*</asp:RequiredFieldValidator>
            <asp:RangeValidator runat="server" ControlToValidate="txtNewStock"
                ValidationGroup="AddProduct" MinimumValue="0" MaximumValue="9999" Type="Integer"
                CssClass="err" ErrorMessage="Stock must be between 0 and 9999." Display="Dynamic">Range err</asp:RangeValidator>
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

    <%-- Manage Products: Filters + GridView --%>
    <asp:UpdatePanel ID="upProducts" runat="server" UpdateMode="Conditional">
    <ContentTemplate>
    <div class="card">
        <div class="ch" style="flex-wrap:wrap;gap:8px">
            <span class="ct">Manage Products</span>
            <div class="filter-bar" style="margin-left:4px">
                <asp:DropDownList ID="ddlFilter" runat="server" AutoPostBack="true"
                    CssClass="fsel" OnSelectedIndexChanged="ddlFilter_Changed" />
                <asp:DropDownList ID="ddlActiveFilter" runat="server" CssClass="fsel" AutoPostBack="true" OnSelectedIndexChanged="ddlFilter_Changed">
                    <asp:ListItem Text="Active only"   Value="active"   Selected="True" />
                    <asp:ListItem Text="Inactive only" Value="inactive" />
                    <asp:ListItem Text="All"           Value="" />
                </asp:DropDownList>
            </div>
            <div style="display:flex;align-items:center;gap:6px;font-size:12px;color:#7a7770;margin-left:auto">
                <span>Per page</span>
                <asp:DropDownList ID="ddlPageSizeP" runat="server" CssClass="fsel" AutoPostBack="true" OnSelectedIndexChanged="ddlPageSizeP_Changed">
                    <asp:ListItem Text="5"  Value="5"  Selected="True" />
                    <asp:ListItem Text="10" Value="10" />
                    <asp:ListItem Text="25" Value="25" />
                    <asp:ListItem Text="50" Value="50" />
                </asp:DropDownList>
            </div>
            <asp:Label ID="lblRowCount" runat="server" CssClass="muted" style="font-family:'DM Mono',monospace" />
        </div>

        <%-- GridView: sort, page, select, inline edit, delete --%>
        <asp:GridView ID="gvProducts" runat="server"
            AutoGenerateColumns="false"
            AllowSorting="true"
            AllowPaging="true"
            PageSize="5"
            DataKeyNames="Id"
            CssClass="grid"
            GridLines="None"
            OnSorting="gvProducts_Sorting"
            OnPageIndexChanging="gvProducts_PageIndexChanging"
            OnSelectedIndexChanged="gvProducts_SelectedIndexChanged"
            OnRowCreated="gvProducts_RowCreated"
            OnRowEditing="gvProducts_RowEditing"
            OnRowUpdating="gvProducts_RowUpdating"
            OnRowCancelingEdit="gvProducts_RowCancelingEdit"
            OnRowDataBound="gvProducts_RowDataBound"
            SelectedRowStyle-CssClass="selected-row">
            <Columns>
                <asp:BoundField DataField="Id" HeaderText="ID" SortExpression="Id" ReadOnly="true" ItemStyle-Width="40px" />

                <asp:TemplateField HeaderText="Name" SortExpression="Name">
                    <ItemTemplate><%# Eval("Name") %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:TextBox ID="txtEditName" runat="server" Text='<%# Bind("Name") %>' Width="160px" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="Category" SortExpression="Category">
                    <ItemTemplate><%# Eval("Category") %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:DropDownList ID="ddlEditCategory" runat="server" SelectedValue='<%# Bind("Category") %>' />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="Price" SortExpression="Price" ItemStyle-Width="80px">
                    <ItemTemplate>$<%# Eval("Price", "{0:F2}") %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:TextBox ID="txtEditPrice" runat="server" Text='<%# Bind("Price") %>' Width="70px" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="Stock" SortExpression="Stock" ItemStyle-Width="65px">
                    <ItemTemplate><%# Eval("Stock") %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:TextBox ID="txtEditStock" runat="server" Text='<%# Bind("Stock") %>' Width="55px" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="Status" ItemStyle-Width="80px">
                    <ItemTemplate>
                        <%# (bool)Eval("IsDeleted")
                            ? "<span class='badge badge-gray'>Deleted</span>"
                            : ((bool)Eval("IsActive") && (int)Eval("Stock") > 0)
                                ? "<span class='badge b-active'>Active</span>"
                                : "<span class='badge badge-gray'>Inactive</span>" %>
                    </ItemTemplate>
                    <EditItemTemplate>
                        <asp:CheckBox ID="chkEditActive" runat="server" Checked='<%# Bind("IsActive") %>' />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:CommandField ShowSelectButton="true" ShowEditButton="true" ShowDeleteButton="false"
                    SelectText="Details" EditText="Edit"
                    ButtonType="Link" ItemStyle-Width="100px" ControlStyle-CssClass="action-link" />
            </Columns>
            <EmptyDataTemplate>
                <p style="padding:16px;color:#9ca3af">No products match the current filter.</p>
            </EmptyDataTemplate>
        </asp:GridView>
    </div>

    <%-- DetailsView shown when row selected --%>
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
    </ContentTemplate>
    </asp:UpdatePanel>

</asp:Content>
