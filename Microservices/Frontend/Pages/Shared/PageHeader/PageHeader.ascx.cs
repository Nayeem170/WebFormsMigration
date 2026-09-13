using System.Web.UI;

namespace CoreWebForms
{
    public partial class PageHeaderControl : UserControl
    {
        public string Title    { get; set; } = null!;
        public string Subtitle { get; set; } = null!;
    }
}
