using System.Drawing;
using System.Windows.Forms;

namespace NORS.ServerGUI
{
    /// <summary>
    /// Double-buffered, owner-drawn dark ListView (Details view) for a flat modern look:
    /// dark header, alternating rows, accent selection. Double buffering also removes the flicker
    /// from the periodic refresh.
    /// </summary>
    internal sealed class ModernListView : ListView
    {
        public ModernListView()
        {
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            OwnerDraw = true;
            BorderStyle = BorderStyle.None;
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.UiFont;
            Dock = DockStyle.Fill;
        }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Header)) e.Graphics.FillRectangle(b, e.Bounds);
            var r = e.Bounds; r.X += 6; r.Width -= 8;
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.HeaderFont, r, Theme.SubtleText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(Theme.Bg))
                e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
        }

        // Whole-row drawing is handled per-cell in OnDrawSubItem.
        protected override void OnDrawItem(DrawListViewItemEventArgs e) => e.DrawDefault = false;

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            Color bg = selected ? Theme.Selection : (e.ItemIndex % 2 == 0 ? Theme.Surface : Theme.SurfaceAlt);
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);

            var r = e.Bounds; r.X += 6; r.Width -= 8;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, r, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
