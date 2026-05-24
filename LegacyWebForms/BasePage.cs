using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public class BasePage : Page
    {
        protected void ApplySortArrow(GridView gv, GridViewRowEventArgs e, string fieldKey, string dirKey)
        {
            if (e.Row.RowType != DataControlRowType.Header) return;
            string field = ViewState[fieldKey]?.ToString();
            string dir = ViewState[dirKey]?.ToString();
            if (field == null) return;
            for (int i = 0; i < gv.Columns.Count; i++)
            {
                if (gv.Columns[i].SortExpression != field) continue;
                var cell = e.Row.Cells[i];
                string arrow = dir == "ASC" ? " &#8593;" : " &#8595;";
                if (cell.Controls.Count > 0 && cell.Controls[0] is LinkButton lb)
                    lb.Text += arrow;
                else
                    cell.Text += arrow;
                break;
            }
        }

        protected void ToggleSortDirection(GridViewSortEventArgs e, string fieldKey, string dirKey)
        {
            string cur = ViewState[dirKey]?.ToString() ?? "ASC";
            ViewState[fieldKey] = e.SortExpression;
            ViewState[dirKey] = cur == "ASC" ? "DESC" : "ASC";
        }
    }
}
