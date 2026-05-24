namespace LegacyWebForms
{
    public partial class ProductsPage
    {
        protected System.Web.UI.WebControls.LinkButton lnkAddProduct;
        protected System.Web.UI.WebControls.Panel pnlAdd;
        protected System.Web.UI.WebControls.ValidationSummary vsSummary;
        protected System.Web.UI.WebControls.Label lblAddResult;
        protected System.Web.UI.WebControls.TextBox txtNewName;
        protected System.Web.UI.WebControls.DropDownList ddlNewCategory;
        protected System.Web.UI.WebControls.TextBox txtNewPrice;
        protected System.Web.UI.WebControls.TextBox txtNewStock;
        protected System.Web.UI.WebControls.CheckBox chkNewActive;
        protected System.Web.UI.WebControls.Button btnSaveNew;
        protected System.Web.UI.WebControls.Button btnCancelNew;
        protected System.Web.UI.WebControls.Literal litProdTotal;
        protected System.Web.UI.WebControls.Literal litProdActive;
        protected System.Web.UI.WebControls.Literal litProdLow;
        protected System.Web.UI.WebControls.Literal litProdOos;
        protected System.Web.UI.UpdatePanel upProducts;
        protected System.Web.UI.WebControls.DropDownList ddlFilter;
        protected System.Web.UI.WebControls.DropDownList ddlPageSizeP;
        protected System.Web.UI.WebControls.DropDownList ddlActiveFilter;
        protected System.Web.UI.WebControls.Label lblRowCount;
        protected System.Web.UI.WebControls.GridView gvProducts;
        protected System.Web.UI.WebControls.HiddenField hdnDeleteProductId;
        protected System.Web.UI.WebControls.Button btnConfirmDeleteProduct;
        protected System.Web.UI.WebControls.Panel pnlDetail;
        protected System.Web.UI.WebControls.LinkButton lnkCloseDetail;
        protected System.Web.UI.WebControls.Literal detId;
        protected System.Web.UI.WebControls.Literal detName;
        protected System.Web.UI.WebControls.Literal detCat;
        protected System.Web.UI.WebControls.Literal detPrice;
        protected System.Web.UI.WebControls.Literal detStock;
        protected System.Web.UI.WebControls.Literal detStatus;
        protected System.Web.UI.WebControls.Literal detAdded;
    }
}
