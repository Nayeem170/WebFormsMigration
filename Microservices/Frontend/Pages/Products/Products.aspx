<%@ Page Title="Products" Language="C#" MasterPageFile="~/Layout/Site.Master" AutoEventWireup="true" CodeBehind="Products.aspx.cs" Inherits="CoreWebForms.ProductsPage" %>
<%@ Register TagPrefix="uc" TagName="PageHeader"      Src="~/Pages/Shared/PageHeader/PageHeader.ascx"  %>
<%@ Register TagPrefix="uc" TagName="ProductSummary"  Src="ProductSummary/ProductSummary.ascx"  %>
<%@ Register TagPrefix="uc" TagName="AddProductPanel"  Src="AddProductPanel/AddProductPanel.ascx"  %>
<%@ Register TagPrefix="uc" TagName="ProductDetail"    Src="ProductDetail/ProductDetail.ascx"    %>

<asp:Content ID="HeadContent" ContentPlaceHolderID="HeadContent" runat="server">
    <link href="<%= ResolveUrl("~/Pages/Products/products.css") %>" rel="stylesheet" />
</asp:Content>

<asp:Content ID="BodyContent" ContentPlaceHolderID="MainContent" runat="server">

    <uc:PageHeader runat="server" Title="Products" Subtitle="Browse and manage inventory" />

    <uc:ProductSummary ID="productSummary" runat="server" />

    <uc:AddProductPanel ID="addProduct" runat="server" OnProductAdded="addProduct_ProductAdded" />

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
            SelectedRowStyle-CssClass="selected-row">
            <Columns>
                <asp:BoundField DataField="Id" HeaderText="ID" SortExpression="Id" ReadOnly="true" ItemStyle-Width="40px" />

                <asp:TemplateField HeaderText="Name" SortExpression="Name">
                    <ItemTemplate><%#: ((CoreWebForms.Product)Container.DataItem).Name %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:TextBox ID="txtEditName" runat="server" Text='<%# ((CoreWebForms.Product)Container.DataItem).Name %>' Width="160px" MaxLength="200" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="Category" SortExpression="Category">
                    <ItemTemplate><%#: ((CoreWebForms.Product)Container.DataItem).Category %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:DropDownList ID="ddlEditCategory" runat="server" SelectedValue='<%# ((CoreWebForms.Product)Container.DataItem).Category %>' />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="Price" SortExpression="Price" ItemStyle-Width="80px">
                    <ItemTemplate>$<%#: ((CoreWebForms.Product)Container.DataItem).Price.ToString("F2") %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:TextBox ID="txtEditPrice" runat="server" Text='<%# ((CoreWebForms.Product)Container.DataItem).Price %>' Width="70px" MaxLength="10" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="Stock" SortExpression="Stock" ItemStyle-Width="65px">
                    <ItemTemplate><%#: ((CoreWebForms.Product)Container.DataItem).Stock %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:TextBox ID="txtEditStock" runat="server" Text='<%# ((CoreWebForms.Product)Container.DataItem).Stock %>' Width="55px" MaxLength="7" />
                    </EditItemTemplate>
                </asp:TemplateField>

                <asp:TemplateField HeaderText="Status" ItemStyle-Width="80px">
                    <ItemTemplate><%# GetStatusHtml(Container.DataItem) %></ItemTemplate>
                    <EditItemTemplate>
                        <asp:CheckBox ID="chkEditActive" runat="server" Checked='<%# ((CoreWebForms.Product)Container.DataItem).IsActive %>' />
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

    <uc:ProductDetail ID="productDetail" runat="server" OnDetailClosed="productDetail_DetailClosed" />
    </ContentTemplate>
    </asp:UpdatePanel>

</asp:Content>
