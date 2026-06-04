namespace LegacyWebForms
{
    public partial class OrderWizardControl
    {
        protected System.Web.UI.HtmlControls.HtmlGenericControl productCombo;
        protected System.Web.UI.WebControls.Panel pnlStep1;
        protected System.Web.UI.WebControls.Panel pnlStep2;
        protected System.Web.UI.WebControls.Panel pnlStep3;
        protected System.Web.UI.WebControls.MultiView mvOrder;
        protected System.Web.UI.WebControls.View vStep1;
        protected System.Web.UI.WebControls.View vStep2;
        protected System.Web.UI.WebControls.View vStep3;
        protected System.Web.UI.WebControls.ValidationSummary vs1;
        protected System.Web.UI.WebControls.DropDownList ddlProduct;
        protected System.Web.UI.WebControls.TextBox txtQty;
        protected System.Web.UI.WebControls.Button btnAddItem;
        protected System.Web.UI.WebControls.Label lblCartWarning;
        protected System.Web.UI.WebControls.Panel pnlCart;
        protected System.Web.UI.WebControls.Repeater rptCart;
        protected System.Web.UI.WebControls.Literal litTotal;
        protected System.Web.UI.WebControls.TextBox txtCustomerName;
        protected System.Web.UI.WebControls.TextBox txtEmail;
        protected System.Web.UI.WebControls.RadioButtonList rblPriority;
        protected System.Web.UI.WebControls.CheckBoxList cblExtras;
        protected System.Web.UI.UpdatePanel upCalendar;
        protected System.Web.UI.WebControls.LinkButton btnCalToday;
        protected System.Web.UI.WebControls.Calendar calDelivery;
        protected System.Web.UI.WebControls.CustomValidator cvDate;
        protected System.Web.UI.WebControls.Button btnNext;
        protected System.Web.UI.WebControls.Repeater rptRevItems;
        protected System.Web.UI.WebControls.Literal litRevTotal;
        protected System.Web.UI.WebControls.Literal litRevCustomer;
        protected System.Web.UI.WebControls.Literal litRevEmail;
        protected System.Web.UI.WebControls.Literal litRevPriority;
        protected System.Web.UI.WebControls.Literal litRevDate;
        protected System.Web.UI.WebControls.Literal litRevExtras;
        protected System.Web.UI.WebControls.Button btnBack;
        protected System.Web.UI.WebControls.Button btnConfirm;
        protected System.Web.UI.WebControls.Literal litOrderId;
        protected System.Web.UI.WebControls.Repeater rptConfirmItems;
        protected System.Web.UI.WebControls.Literal litConfTotal;
        protected System.Web.UI.WebControls.Literal litConfCustomer;
        protected System.Web.UI.WebControls.Literal litConfEmail;
        protected System.Web.UI.WebControls.Literal litConfDate;
        protected System.Web.UI.WebControls.Literal litConfPriority;
        protected System.Web.UI.HtmlControls.HtmlTableRow trConfExtras;
        protected System.Web.UI.WebControls.Literal litConfExtras;
        protected System.Web.UI.WebControls.Button btnNewOrder;
    }
}
