using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CDriveCleaner
{
    static class Theme
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }

        public static readonly Color Bg = Color.FromArgb(243, 245, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 230, 237);
        public static readonly Color Text = Color.FromArgb(26, 32, 44);
        public static readonly Color Muted = Color.FromArgb(100, 110, 128);
        public static readonly Color Faint = Color.FromArgb(160, 168, 182);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
        public static readonly Color AccentSoft = Color.FromArgb(232, 239, 253);
        public static readonly Color Track = Color.FromArgb(232, 235, 241);
        public static readonly Color Sidebar = Color.FromArgb(17, 24, 39);
        public static readonly Color SidebarHover = Color.FromArgb(31, 41, 59);
        public static readonly Color SidebarText = Color.FromArgb(203, 210, 222);
        public static readonly Color Green = Color.FromArgb(22, 145, 88);
        public static readonly Color Amber = Color.FromArgb(202, 128, 4);
        public static readonly Color Red = Color.FromArgb(214, 54, 54);
        public static readonly Color Purple = Color.FromArgb(124, 77, 220);

        public static Font Title, H2, Body, BodyBold, Small, SmallBold, Big, Icon, IconBig, IconSmall;
        static string iconFamily;

        public static void Init(float dpi)
        {
            Scale = dpi / 96f;
            Title = new Font("Segoe UI Semibold", 19f);
            H2 = new Font("Segoe UI Semibold", 12.5f);
            Big = new Font("Segoe UI Semibold", 22f);
            Body = new Font("Segoe UI", 10f);
            BodyBold = new Font("Segoe UI Semibold", 10f);
            Small = new Font("Segoe UI", 9f);
            SmallBold = new Font("Segoe UI Semibold", 8.5f);
            iconFamily = "Segoe MDL2 Assets";
            using (var fonts = new InstalledFontCollection())
                foreach (var f in fonts.Families)
                    if (f.Name == "Segoe Fluent Icons") iconFamily = f.Name;
            Icon = new Font(iconFamily, 11f);
            IconBig = new Font(iconFamily, 20f);
            IconSmall = new Font(iconFamily, 8.5f);
        }

        // Icon glyphs (Segoe MDL2 / Fluent)
        public const string GHome = "\uE80F", GBroom = "\uEA99", GPie = "\uEB05", GApps = "\uE71D", GShield = "\uEA18",
            GFolder = "\uE8B7", GFile = "\uE8A5", GUp = "\uE74A", GOpen = "\uE8DA", GDrive = "\uEDA2", GCheck = "\uE73E",
            GInfo = "\uE946", GSearch = "\uE721", GRefresh = "\uE72C", GSettings = "\uE713", GWarning = "\uE7BA", GChevron = "\uE76C",
            GDelete = "\uE74D", GStop = "\uE71A";

        public static Color ForLevel(Safety s)
        {
            switch (s)
            {
                case Safety.Safe: return Green;
                case Safety.Review: return Amber;
                case Safety.Personal: return Purple;
                case Safety.App: return Accent;
                default: return Red;
            }
        }

        public static string LevelName(Safety s)
        {
            switch (s)
            {
                case Safety.Safe: return "Safe to delete";
                case Safety.Review: return "Review first";
                case Safety.Personal: return "Your files";
                case Safety.App: return "Program";
                default: return "Keep - system";
            }
        }

        public static Color Soft(Color c) { return Blend(c, Color.White, 0.88f); }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color? border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.HasValue) using (var pen = new Pen(border.Value)) g.DrawPath(pen, path);
            }
        }

        public static void Bar(Graphics g, Rectangle r, double value, Color fill)
        {
            int rad = r.Height / 2;
            FillRound(g, r, rad, Track, null);
            int w = (int)Math.Round(r.Width * Math.Max(0, Math.Min(1, value)));
            if (w > 0) FillRound(g, new Rectangle(r.X, r.Y, Math.Max(w, r.Height), r.Height), rad, fill, null);
        }

        public static void Pill(Graphics g, Point at, string text, Color c, out int width)
        {
            var sz = TextRenderer.MeasureText(g, text, SmallBold, Size.Empty, TextFormatFlags.NoPadding);
            var r = new Rectangle(at.X, at.Y, sz.Width + S(14), sz.Height + S(6));
            FillRound(g, r, r.Height / 2, Soft(c), null);
            TextRenderer.DrawText(g, text, SmallBold, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            width = r.Width;
        }

        public static void Draw(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags extra = 0)
        {
            TextRenderer.DrawText(g, s, f, r, c, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | extra);
        }

        static System.Drawing.Icon appIcon;
        public static System.Drawing.Icon AppIcon { get { if (appIcon == null) appIcon = MakeIcon(); return appIcon; } }

        static System.Drawing.Icon MakeIcon()
        {
            using (var bmp = new Bitmap(64, 64))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = Round(new Rectangle(2, 2, 59, 59), 14))
                    using (var b = new LinearGradientBrush(new Rectangle(0, 0, 64, 64), Color.FromArgb(59, 130, 246), Color.FromArgb(29, 78, 216), 90f))
                        g.FillPath(b, path);
                    using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 9f)) g.DrawEllipse(pen, 15, 15, 34, 34);
                    using (var pen = new Pen(Color.White, 9f)) g.DrawArc(pen, 15, 15, 34, 34, -90, 250);
                    using (var pen = new Pen(Color.FromArgb(74, 222, 128), 9f)) g.DrawArc(pen, 15, 15, 34, 34, 160, 110);
                }
                return System.Drawing.Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    // A rounded button that looks the same on every Windows version.
    class FlatButton : Control
    {
        public enum Style { Primary, Secondary, Danger, Ghost }
        public Style Kind = Style.Primary;
        public string Glyph;
        bool hover, down;

        public FlatButton(string text, Style kind = Style.Primary, string glyph = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = text; Kind = kind; Glyph = glyph;
            Font = Theme.BodyBold;
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = Theme.S(38);
            FitWidth();
        }

        public void FitWidth()
        {
            int w = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + Theme.S(32);
            if (Glyph != null) w += Theme.S(24);
            Width = w;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Color bg, fg; Color? border = null;
            switch (Kind)
            {
                case Style.Primary:
                    bg = down ? Theme.AccentDark : hover ? Theme.Blend(Theme.Accent, Theme.AccentDark, 0.5f) : Theme.Accent; fg = Color.White; break;
                case Style.Danger:
                    bg = hover ? Theme.Soft(Theme.Red) : Color.White; fg = Theme.Red; border = Theme.Blend(Theme.Red, Color.White, 0.55f); break;
                case Style.Ghost:
                    bg = hover ? Theme.AccentSoft : (Parent != null ? Parent.BackColor : Color.White); fg = Theme.Accent; break;
                default:
                    bg = down ? Theme.Track : hover ? Theme.Bg : Color.White; fg = Theme.Text; border = Theme.Border; break;
            }
            if (!Enabled) { bg = Theme.Track; fg = Theme.Faint; border = null; }
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.S(8), bg, border);
            if (Focused && ShowFocusCues)
                using (var path = Theme.Round(Rectangle.Inflate(r, -2, -2), Theme.S(7)))
                using (var pen = new Pen(Kind == Style.Primary ? Color.White : Theme.Accent) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, path);

            var ts = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int gw = Glyph != null ? Theme.S(24) : 0;
            int x = (Width - ts.Width - gw) / 2;
            if (Glyph != null)
                TextRenderer.DrawText(g, Glyph, Theme.Icon, new Rectangle(x, 0, Theme.S(18), Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gw, 0, ts.Width + 2, Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    // Left menu entry.
    class NavButton : Control
    {
        public string Glyph;
        public bool Selected;
        bool hover;

        public NavButton(string text, string glyph)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = text; Glyph = glyph;
            Height = Theme.S(46);
            Cursor = Cursors.Hand;
            TabStop = true;
            Font = Theme.BodyBold;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Sidebar);
            var r = new Rectangle(Theme.S(12), Theme.S(3), Width - Theme.S(24), Height - Theme.S(6));
            if (Selected) Theme.FillRound(g, r, Theme.S(8), Theme.Accent, null);
            else if (hover || Focused) Theme.FillRound(g, r, Theme.S(8), Theme.SidebarHover, null);
            Color fg = Selected ? Color.White : Theme.SidebarText;
            TextRenderer.DrawText(g, Glyph, Theme.Icon, new Rectangle(r.X + Theme.S(14), r.Y, Theme.S(24), r.Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(r.X + Theme.S(46), r.Y, r.Width - Theme.S(50), r.Height), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // White rounded panel.
    class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
            Padding = new Padding(Theme.S(20));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(12), Theme.Card, Theme.Border);
        }
    }

    // A panel that only paints (no flicker).
    class PaintBox : Panel
    {
        public event PaintEventHandler Painting;
        public PaintBox()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Painting != null) Painting(this, e);
        }
    }

    class SmoothList : ListView
    {
        public SmoothList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            BorderStyle = BorderStyle.None;
            OwnerDraw = true;
            Font = Theme.Body;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            DrawColumnHeader += (s, e) =>
            {
                e.Graphics.FillRectangle(Brushes.White, e.Bounds);
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var flags = e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
                var r = new Rectangle(e.Bounds.X + Theme.S(10), e.Bounds.Y, e.Bounds.Width - Theme.S(20), e.Bounds.Height);
                Theme.Draw(e.Graphics, e.Header.Text.ToUpperInvariant(), Theme.SmallBold, r, Theme.Muted, flags | TextFormatFlags.VerticalCenter);
            };
            DrawItem += (s, e) => { e.DrawDefault = false; };
        }

        public void SetRowHeight(int h)
        {
            var il = new ImageList();
            il.ImageSize = new Size(1, h);
            SmallImageList = il;
        }

        // Stretches one column so the list always fills its width.
        public void Stretch(int column)
        {
            if (Columns.Count == 0) return;
            int used = 0;
            for (int i = 0; i < Columns.Count; i++) if (i != column) used += Columns[i].Width;
            int w = ClientSize.Width - used - 2;
            if (w > Theme.S(120)) Columns[column].Width = w;
        }

        public void RowBackground(DrawListViewSubItemEventArgs e)
        {
            Color bg = e.Item.Selected ? Theme.AccentSoft : Color.White;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var pen = new Pen(Color.FromArgb(240, 242, 246))) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        public static Rectangle Inner(Rectangle b) { return new Rectangle(b.X + Theme.S(10), b.Y, b.Width - Theme.S(20), b.Height); }
    }

    static class Lay
    {
        // Adds controls docked to the top so they appear in the given order.
        public static void Stack(Control parent, params Control[] topDown)
        {
            for (int i = topDown.Length - 1; i >= 0; i--)
            {
                topDown[i].Dock = DockStyle.Top;
                parent.Controls.Add(topDown[i]);
            }
        }

        public static Panel Gap(int h)
        {
            return new Panel { Height = Theme.S(h), Dock = DockStyle.Top, BackColor = Color.Transparent };
        }

        public static Label Label(string text, Font f, Color c, int height)
        {
            return new Label { Text = text, Font = f, ForeColor = c, Height = Theme.S(height), AutoSize = false, AutoEllipsis = true, BackColor = Color.Transparent };
        }

        public static Panel Row(int height, params Control[] rightToLeft)
        {
            // A strip with buttons pushed to the right edge.
            var p = new Panel { Height = Theme.S(height), BackColor = Color.Transparent };
            p.Resize += (s, e) => Arrange(p, rightToLeft);
            foreach (var c in rightToLeft) p.Controls.Add(c);
            return p;
        }

        public static void Arrange(Control p, Control[] rightToLeft)
        {
            int x = p.ClientSize.Width;
            foreach (var c in rightToLeft)
            {

                x -= c.Width;
                c.Location = new Point(x, (p.ClientSize.Height - c.Height) / 2);
                x -= Theme.S(10);
            }
        }
    }
}
