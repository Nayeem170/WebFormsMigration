using System.Web.UI;
using System.Web.UI.WebControls;

namespace LegacyWebForms
{
    public static class GridViewHelper
    {
        public static void ApplySortArrow(GridView gv, GridViewRowEventArgs e, string fieldKey, string dirKey, StateBag viewState)
        {
            if (e.Row.RowType != DataControlRowType.Header) return;
            string? field = viewState[fieldKey] as string;
            string? dir = viewState[dirKey] as string;
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

        public static void ToggleSortDirection(GridViewSortEventArgs e, string fieldKey, string dirKey, StateBag viewState)
        {
            string cur   = viewState[dirKey] as string ?? "ASC";
            string? field = viewState[fieldKey] as string;
            viewState[fieldKey] = e.SortExpression;
            viewState[dirKey]   = field == e.SortExpression ? (cur == "ASC" ? "DESC" : "ASC") : "ASC";
        }
    }
}
