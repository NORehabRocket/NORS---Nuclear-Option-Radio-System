using System.Drawing;
using System.Windows.Forms;

namespace NORS.ServerGUI
{
    /// <summary>Flat dark theme palette + helpers for a modern WinForms look.</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(30, 30, 30);
        public static readonly Color Surface = Color.FromArgb(37, 37, 38);
        public static readonly Color SurfaceAlt = Color.FromArgb(45, 45, 48);
        public static readonly Color Header = Color.FromArgb(45, 45, 48);
        public static readonly Color Border = Color.FromArgb(62, 62, 66);
        public static readonly Color Selection = Color.FromArgb(9, 71, 113);
        public static readonly Color Accent = Color.FromArgb(0, 122, 204);
        public static readonly Color AccentHover = Color.FromArgb(28, 151, 234);
        public static readonly Color Danger = Color.FromArgb(170, 60, 60);
        public static readonly Color DangerHover = Color.FromArgb(205, 80, 80);
        public static readonly Color Neutral = Color.FromArgb(60, 60, 64);
        public static readonly Color NeutralHover = Color.FromArgb(80, 80, 86);
        public static readonly Color Text = Color.FromArgb(220, 220, 220);
        public static readonly Color SubtleText = Color.FromArgb(150, 150, 150);
        public static readonly Color Good = Color.FromArgb(78, 201, 176);

        public static readonly Font UiFont = new Font("Segoe UI", 9f);
        public static readonly Font HeaderFont = new Font("Segoe UI Semibold", 8.25f);
        public static readonly Font MonoFont = new Font("Consolas", 9f);

        public static Button StyleButton(Button b, Color back, Color hover)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = back;
            b.ForeColor = Text;
            b.Font = UiFont;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = hover;
            b.Cursor = Cursors.Hand;
            b.Height = 28;
            b.Margin = new Padding(4, 3, 4, 3);
            return b;
        }

        public static TextBox StyleInput(TextBox t)
        {
            t.BackColor = SurfaceAlt;
            t.ForeColor = Text;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = UiFont;
            t.Margin = new Padding(4, 4, 4, 4);
            return t;
        }

        public static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = SubtleText, Font = UiFont, Padding = new Padding(6, 7, 2, 0) };
        }
    }
}
