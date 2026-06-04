using System;
using System.Linq;
using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public partial class OrderHistoryControl : UserControl
    {
        private const int HistoryPageSize = 10;

        public bool ShowDeleted { get; set; } = true;

        private int HistoryPage
        {
            get { return (int)(ViewState["HistoryPage"] ?? 0); }
            set { ViewState["HistoryPage"] = value; }
        }

        public void Bind()
        {
            int total = AppData.Services.Orders.Count(includeDeleted: ShowDeleted, status: null);
            int pages = (int)Math.Ceiling((double)total / HistoryPageSize);
            if (pages == 0) HistoryPage = 0;
            else if (HistoryPage >= pages) HistoryPage = pages - 1;
            if (HistoryPage < 0) HistoryPage = 0;

            var page = AppData.Services.Orders.GetPaged(HistoryPage * HistoryPageSize, HistoryPageSize, ShowDeleted, null);
            ordersTable.Bind(page);

            lblHistPage.Text = pages > 0 ? string.Format("{0} / {1}", HistoryPage + 1, pages) : "";
            lnkHistPrev.Visible = HistoryPage > 0;
            lnkHistNext.Visible = HistoryPage < pages - 1;
            upHistory.Update();
        }

        protected void lnkHistPrev_Click(object sender, EventArgs e)
        {
            HistoryPage--;
            Bind();
        }

        protected void lnkHistNext_Click(object sender, EventArgs e)
        {
            HistoryPage++;
            Bind();
        }
    }
}
