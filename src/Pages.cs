using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace CDriveCleaner
{
    class Page : Panel
    {
        protected readonly MainForm F;
        protected readonly AppState St;
        protected static int S(int v) { return Theme.S(v); }

        public Page(MainForm f)
        {
            F = f; St = f.State;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            Padding = new Padding(S(32), S(24), S(32), S(24));
        }

        protected List<Control> Header(string title, string sub)
        {
            return new List<Control> { Lay.Label(title, Theme.Title, Theme.Text, 42), Lay.Label(sub, Theme.Body, Theme.Muted, 26), Lay.Gap(14) };
        }

        protected static void IconCircle(Graphics g, Rectangle r, string glyph, Color c)
        {
            Theme.FillRound(g, r, r.Width / 2, Theme.Soft(c), null);
            TextRenderer.DrawText(g, glyph, Theme.IconBig, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Shared look for "scan in progress" on Home and on the space page.
        protected void ScanLines(DriveScanner r, out string title, out string line1, out string line2)
        {
            title = r.CancelRequested ? "Stopping..." : "Scanning your C: drive...  " + (int)(St.ScanFraction * 100) + "%";
            line1 = Fmt.Count(Interlocked.Read(ref r.FilesSeen)) + " files  -  " + Fmt.Size(Interlocked.Read(ref r.BytesSeen)) + " checked so far";
            line2 = r.Current;
        }

        protected static DialogResult Ask(IWin32Window owner, string text, string caption, MessageBoxIcon icon = MessageBoxIcon.Question)
        {
            return MessageBox.Show(owner, text, caption, MessageBoxButtons.YesNo, icon);
        }
    }

    // ---------------------------------------------------------------- Home

    class TileRow
    {
        public string Label, Value;
        public double Fraction = -1;
        public object Tag;
    }

    class Tile : Card
    {
        public string Heading, BigText = "", SubText = "";
        public Color BigColor = Theme.Text;
        public List<TileRow> Rows = new List<TileRow>();
        public readonly FlatButton Button;
        public event Action<TileRow> RowClicked;
        readonly List<Rectangle> rects = new List<Rectangle>();
        int hover = -1;

        public Tile(string heading, string button)
        {
            Heading = heading;
            Button = new FlatButton(button, FlatButton.Style.Secondary);
            Controls.Add(Button);
            Resize += (s, e) => Button.Location = new Point(Theme.S(20), Height - Theme.S(20) - Button.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int p = Theme.S(20), y = Theme.S(18), w = Width - p * 2;
            Theme.Draw(g, Heading, Theme.SmallBold, new Rectangle(p, y, w, Theme.S(18)), Theme.Muted); y += Theme.S(24);
            Theme.Draw(g, BigText, Theme.Big, new Rectangle(p, y, w, Theme.S(44)), BigColor); y += Theme.S(44);
            Theme.Draw(g, SubText, Theme.Small, new Rectangle(p, y, w, Theme.S(20)), Theme.Muted); y += Theme.S(30);
            rects.Clear();
            int bottom = Button.Top - Theme.S(8);
            for (int i = 0; i < Rows.Count; i++)
            {
                if (y + Theme.S(38) > bottom) break;
                var row = Rows[i];
                var rr = new Rectangle(p - Theme.S(8), y - Theme.S(4), w + Theme.S(16), Theme.S(40));
                if (i == hover && row.Tag != null) Theme.FillRound(g, rr, Theme.S(6), Theme.AccentSoft, null);
                Theme.Draw(g, row.Label, Theme.Body, new Rectangle(p, y, w - Theme.S(90), Theme.S(20)), Theme.Text);
                Theme.Draw(g, row.Value, Theme.BodyBold, new Rectangle(p + w - Theme.S(90), y, Theme.S(90), Theme.S(20)), Theme.Text, TextFormatFlags.Right);
                if (row.Fraction >= 0) Theme.Bar(g, new Rectangle(p, y + Theme.S(24), w, Theme.S(5)), row.Fraction, Theme.Accent);
                rects.Add(rr);
                y += Theme.S(42);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = rects.FindIndex(r => r.Contains(e.Location));
            Cursor = h >= 0 && Rows[h].Tag != null ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h >= 0 && Rows[h].Tag != null && RowClicked != null) RowClicked(Rows[h]);
            base.OnMouseClick(e);
        }
    }

    class HomePage : Page
    {
        readonly Card drive, scan;
        readonly FlatButton scanBtn, stopBtn, exploreBtn;
        readonly Tile tClean, tFolders, tApps;

        public HomePage(MainForm f) : base(f)
        {
            drive = new Card { Height = S(124) };
            drive.Paint += (s, e) => PaintDrive(e.Graphics);

            scan = new Card { Height = S(118) };
            scan.Paint += (s, e) => PaintScan(e.Graphics);
            scanBtn = new FlatButton("Scan C: drive", FlatButton.Style.Primary, Theme.GSearch);
            stopBtn = new FlatButton("Stop", FlatButton.Style.Secondary, Theme.GStop);
            exploreBtn = new FlatButton("See what's using space", FlatButton.Style.Primary, Theme.GPie);
            scanBtn.Click += (s, e) => St.StartScan();
            stopBtn.Click += (s, e) => St.CancelScan();
            exploreBtn.Click += (s, e) => F.Go(2);
            scan.Controls.AddRange(new Control[] { scanBtn, stopBtn, exploreBtn });
            scan.Resize += (s, e) => LayoutScan();

            var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg, Margin = Padding.Empty, Padding = Padding.Empty };
            for (int i = 0; i < 3; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            tClean = new Tile("SAFE TO CLEAN", "Review & clean");
            tFolders = new Tile("BIGGEST FOLDERS ON C:", "Explore all folders");
            tApps = new Tile("INSTALLED APPS", "Manage apps");
            tClean.Button.Click += (s, e) => F.Go(1);
            tClean.RowClicked += r => F.Go(1);
            tFolders.Button.Click += (s, e) => { if (St.Scan == null && St.Running == null) St.StartScan(); F.Go(2); };
            tFolders.RowClicked += r => F.ShowFolder((string)r.Tag);
            tApps.Button.Click += (s, e) => F.Go(3);
            tApps.RowClicked += r => F.Go(3);
            var all = new[] { tClean, tFolders, tApps };
            for (int i = 0; i < 3; i++)
            {
                all[i].Dock = DockStyle.Fill;
                all[i].Margin = new Padding(i == 0 ? 0 : S(7), 0, i == 2 ? 0 : S(7), 0);
                tiles.Controls.Add(all[i], i, 0);
            }

            Controls.Add(tiles);
            var top = Header("Let's free up space on your C: drive", "See what's taking up space, what's safe to remove, and which apps are installed.");
            top.AddRange(new Control[] { drive, Lay.Gap(14), scan, Lay.Gap(14) });
            Lay.Stack(this, top.ToArray());

            St.ScanChanged += () => { LayoutScan(); UpdateFolders(); scan.Invalidate(); };
            St.ScanProgress += () => scan.Invalidate();
            St.CleanChanged += UpdateClean;
            St.AppsChanged += UpdateApps;
            St.DriveChanged += () => drive.Invalidate();
            UpdateClean(); UpdateFolders(); UpdateApps(); LayoutScan();
        }

        void PaintDrive(Graphics g)
        {
            int p = S(24);
            IconCircle(g, new Rectangle(p, p, S(52), S(52)), Theme.GDrive, Theme.Accent);
            int x = p + S(70);
            long total = St.DriveTotal, free = St.DriveFree, used = total - free;
            double frac = total > 0 ? (double)used / total : 0;
            Color health = frac > 0.9 ? Theme.Red : frac > 0.8 ? Theme.Amber : Theme.Green;
            int rightW = S(220);
            Theme.Draw(g, "Local Disk (C:)", Theme.H2, new Rectangle(x, p - S(2), drive.Width - x - rightW, S(28)), Theme.Text);
            Theme.Draw(g, total > 0 ? Fmt.Size(used) + " used of " + Fmt.Size(total) : "Reading drive...", Theme.Body,
                new Rectangle(x, p + S(28), drive.Width - x - rightW, S(22)), Theme.Muted);
            var freeRect = new Rectangle(drive.Width - p - rightW, p - S(6), rightW, S(40));
            Theme.Draw(g, Fmt.Size(free) + " free", Theme.Big, freeRect, health, TextFormatFlags.Right);
            string note = frac > 0.9 ? "Your drive is almost full" : frac > 0.8 ? "Getting full" : "Plenty of room";
            Theme.Draw(g, note, Theme.Small, new Rectangle(freeRect.X, freeRect.Bottom, rightW, S(18)), Theme.Muted, TextFormatFlags.Right);
            Theme.Bar(g, new Rectangle(x, drive.Height - S(34), drive.Width - x - p, S(12)), frac, frac > 0.9 ? Theme.Red : Theme.Accent);
        }

        void LayoutScan()
        {
            bool running = St.Running != null, done = St.Scan != null;
            stopBtn.Visible = running;
            scanBtn.Visible = !running;
            exploreBtn.Visible = !running && done;
            var shown = new List<FlatButton>();
            if (!running && done) shown.Add(exploreBtn);
            if (!running) shown.Add(scanBtn); else shown.Add(stopBtn);
            scanBtn.Text = done ? "Scan again" : "Scan C: drive";
            scanBtn.Kind = done ? FlatButton.Style.Secondary : FlatButton.Style.Primary;
            scanBtn.FitWidth();
            int x = scan.Width - S(24);
            buttonsLeft = x;
            foreach (var b in shown)
            {
                x -= b.Width;
                buttonsLeft = x;
                b.Location = new Point(x, S(30));
                x -= S(10);
            }
            scan.Invalidate();
        }

        int buttonsLeft;
        int ButtonsLeft() { return buttonsLeft; }

        void PaintScan(Graphics g)
        {
            int p = S(24);
            int right = ButtonsLeft() - S(16);
            var r = St.Running;
            string title, line1, line2 = null;
            string glyph = Theme.GSearch; Color gc = Theme.Accent;
            if (r != null) ScanLines(r, out title, out line1, out line2);
            else if (St.Scan != null)
            {
                var sc = St.Scan;
                glyph = Theme.GCheck; gc = Theme.Green;
                title = "Scan finished";
                line1 = "Checked " + Fmt.Count(sc.Root.Files) + " files (" + Fmt.Size(sc.Root.Size) + ") in " + Fmt.Duration(sc.Finished - sc.Started) + ".";
                if (sc.DeniedCount > 0 && !Program.IsAdmin())
                    line2 = Fmt.Count(sc.DeniedCount) + " protected folders were skipped. Run as administrator to include them.";
            }
            else
            {
                title = "Find out what's using space";
                line1 = "Looks through every folder on C: and lists the biggest first. Usually takes 1-5 minutes.";
            }
            IconCircle(g, new Rectangle(p, p, S(52), S(52)), glyph, gc);
            int x = p + S(70);
            Theme.Draw(g, title, Theme.H2, new Rectangle(x, S(20), right - x, S(28)), Theme.Text);
            Theme.Draw(g, line1, Theme.Body, new Rectangle(x, S(50), right - x, S(22)), Theme.Muted);
            if (line2 != null) Theme.Draw(g, line2, Theme.Small, new Rectangle(x, S(74), right - x, S(18)), Theme.Faint, TextFormatFlags.PathEllipsis);
            if (r != null) Theme.Bar(g, new Rectangle(x, scan.Height - S(20), scan.Width - x - p, S(6)), St.ScanFraction, Theme.Accent);
        }

        void UpdateClean()
        {
            long safe = St.SafeToClean;
            tClean.BigText = St.Measuring && safe == 0 ? "Checking..." : Fmt.Size(safe);
            tClean.BigColor = Theme.Green;
            tClean.SubText = "Temporary files, caches and Recycle Bin";
            var items = St.CleanItems.Where(i => !i.InfoOnly && i.Size > 0).OrderByDescending(i => i.Size).Take(4).ToList();
            long max = items.Count > 0 ? items[0].Size : 1;
            tClean.Rows = items.Select(i => new TileRow { Label = i.Title, Value = Fmt.Size(i.Size), Fraction = (double)i.Size / max, Tag = "clean" }).ToList();
            tClean.Invalidate();
        }

        void UpdateFolders()
        {
            var sc = St.Scan;
            tFolders.Rows.Clear();
            if (sc == null)
            {
                tFolders.BigText = "-";
                tFolders.BigColor = Theme.Faint;
                tFolders.SubText = "Scan your drive to see this";
                tFolders.Button.Text = St.Running != null ? "View progress" : "Scan now";
            }
            else
            {
                tFolders.BigText = Fmt.Size(sc.Root.Size);
                tFolders.BigColor = Theme.Text;
                tFolders.SubText = "Found in " + Fmt.Count(sc.Root.Files) + " files. Click a folder to look inside.";
                tFolders.Button.Text = "Explore all folders";
                if (sc.Root.Children != null)
                    foreach (var c in sc.Root.Children.Take(5))
                        tFolders.Rows.Add(new TileRow { Label = c.Name, Value = Fmt.Size(c.Size), Fraction = (double)c.Size / Math.Max(1, sc.Root.Size), Tag = c.Path });
            }
            tFolders.Button.FitWidth();
            tFolders.Invalidate();
        }

        void UpdateApps()
        {
            var apps = St.Apps;
            if (St.AppsLoading && apps.Count == 0)
            {
                tApps.BigText = "Loading...";
                tApps.SubText = "";
            }
            else
            {
                tApps.BigText = apps.Count + " apps";
                tApps.SubText = "Your biggest apps:";
            }
            tApps.BigColor = Theme.Text;
            var top = apps.Where(a => a.Size > 0).Take(4).ToList();
            long max = top.Count > 0 ? top[0].Size : 1;
            tApps.Rows = top.Select(a => new TileRow { Label = a.Name, Value = Fmt.Size(a.Size), Fraction = (double)a.Size / max, Tag = "apps" }).ToList();
            tApps.Invalidate();
        }
    }

    // ---------------------------------------------------------------- Clean up

    class CleanRow : Control
    {
        public readonly CleanItem Item;
        public event Action Changed, ActionDone;
        Rectangle linkRect;
        bool hover, linkHover;
        static int S(int v) { return Theme.S(v); }

        public CleanRow(CleanItem item)
        {
            Item = item;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = S(96);
            TabStop = true;
        }

        string LinkText
        {
            get
            {
                if (Item.ActionText != null) return Item.ActionText;
                return Item.OpenPath != null ? "Open folder" : null;
            }
        }

        bool CanCheck { get { return !Item.InfoOnly && !Item.Blocked; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            var card = new Rectangle(0, 0, Width - 1, Height - S(10) - 1);
            Color border = (hover || Focused) && CanCheck ? Theme.Blend(Theme.Border, Theme.Accent, 0.45f) : Theme.Border;
            Theme.FillRound(g, card, S(10), Color.White, border);
            int p = S(20), mid = card.Height / 2;

            if (Item.InfoOnly)
            {
                var ic = new Rectangle(p - S(2), mid - S(14), S(28), S(28));
                Theme.FillRound(g, ic, S(14), Theme.Soft(Theme.ForLevel(Item.Level)), null);
                TextRenderer.DrawText(g, Theme.GInfo, Theme.Icon, ic, Theme.ForLevel(Item.Level), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                var box = new Rectangle(p, mid - S(11), S(22), S(22));
                if (Item.Blocked) Theme.FillRound(g, box, S(5), Theme.Track, Theme.Border);
                else if (Item.Checked)
                {
                    Theme.FillRound(g, box, S(5), Theme.Accent, null);
                    TextRenderer.DrawText(g, Theme.GCheck, Theme.IconSmall, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else Theme.FillRound(g, box, S(5), Color.White, Theme.Blend(Theme.Border, Theme.Muted, 0.4f));
            }

            int x = p + S(44);
            int sizeW = S(200);
            var ts = TextRenderer.MeasureText(g, Item.Title, Theme.BodyBold, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Item.Title, Theme.BodyBold, new Point(x, S(18)), Item.Blocked ? Theme.Muted : Theme.Text, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int pw, px = x + ts.Width + S(10);
            Theme.Pill(g, new Point(px, S(16)), Theme.LevelName(Item.Level), Theme.ForLevel(Item.Level), out pw);
            if (Item.Blocked) Theme.Pill(g, new Point(px + pw + S(6), S(16)), "Needs administrator", Theme.Red, out pw);

            var desc = new Rectangle(x, S(44), Width - x - sizeW - S(30), card.Height - S(48));
            TextRenderer.DrawText(g, Item.Description, Theme.Small, desc, Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            var sizeRect = new Rectangle(Width - p - sizeW, S(14), sizeW, S(28));
            if (!Item.Measured) Theme.Draw(g, "Checking...", Theme.Body, sizeRect, Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            else if (Item.Size == 0) Theme.Draw(g, "Nothing to clean", Theme.Body, sizeRect, Theme.Faint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            else Theme.Draw(g, Fmt.Size(Item.Size), Theme.H2, sizeRect, Item.Blocked ? Theme.Muted : Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            linkRect = Rectangle.Empty;
            string link = LinkText;
            if (link != null)
            {
                var ls = TextRenderer.MeasureText(g, link, Theme.BodyBold, Size.Empty, TextFormatFlags.NoPadding);
                linkRect = new Rectangle(Width - p - ls.Width, S(50), ls.Width, ls.Height + S(4));
                TextRenderer.DrawText(g, link, Theme.BodyBold, linkRect.Location, linkHover ? Theme.AccentDark : Theme.Accent, TextFormatFlags.NoPadding);
                if (linkHover) using (var pen = new Pen(Theme.AccentDark)) g.DrawLine(pen, linkRect.Left, linkRect.Bottom - S(2), linkRect.Right, linkRect.Bottom - S(2));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool lh = linkRect.Contains(e.Location);
            bool h = true;
            Cursor = lh || CanCheck ? Cursors.Hand : Cursors.Default;
            if (lh != linkHover || h != hover) { linkHover = lh; hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = false; linkHover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Focus();
            if (linkRect.Contains(e.Location)) RunLink();
            else Toggle();
            base.OnMouseClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) Toggle();
            if (e.KeyCode == Keys.Enter) RunLink();
            base.OnKeyDown(e);
        }

        void Toggle()
        {
            if (!CanCheck) return;
            Item.Checked = !Item.Checked;
            Invalidate();
            if (Changed != null) Changed();
        }

        void RunLink()
        {
            if (Item.Action != null)
            {
                Item.Action();
                if (ActionDone != null) ActionDone();
            }
            else if (Item.OpenPath != null) Program.OpenInExplorer(Item.OpenPath, false);
        }
    }

    class CleanPage : Page
    {
        readonly Panel list;
        readonly PaintBox adminStrip;
        readonly Card bottom;
        readonly FlatButton cleanBtn, recheckBtn;
        List<CleanItem> shown;
        bool busy;
        string status;

        public CleanPage(MainForm f) : base(f)
        {
            list = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };

            bottom = new Card { Dock = DockStyle.Bottom, Height = S(84) };
            bottom.Paint += (s, e) => PaintBottom(e.Graphics);
            cleanBtn = new FlatButton("Clean selected", FlatButton.Style.Primary, Theme.GBroom) { Height = S(42) };
            cleanBtn.FitWidth();
            recheckBtn = new FlatButton("Check again", FlatButton.Style.Secondary, Theme.GRefresh) { Height = S(42) };
            cleanBtn.Click += (s, e) => DoClean();
            recheckBtn.Click += (s, e) => { St.MeasureCleanup(); St.RefreshDrive(); };
            bottom.Controls.Add(cleanBtn);
            bottom.Controls.Add(recheckBtn);
            bottom.Resize += (s, e) =>
            {
                cleanBtn.Location = new Point(bottom.Width - S(22) - cleanBtn.Width, (bottom.Height - cleanBtn.Height) / 2);
                recheckBtn.Location = new Point(cleanBtn.Left - S(10) - recheckBtn.Width, cleanBtn.Top);
            };

            adminStrip = new PaintBox { Height = S(56), BackColor = Theme.Bg };
            var adminBtn = new FlatButton("Restart as administrator", FlatButton.Style.Primary, Theme.GShield);
            adminBtn.Click += (s, e) => F.AskRestartAsAdmin();
            adminStrip.Controls.Add(adminBtn);
            adminStrip.Resize += (s, e) => adminBtn.Location = new Point(adminStrip.Width - S(10) - adminBtn.Width, (adminStrip.Height - S(10) - adminBtn.Height) / 2);
            adminStrip.Painting += (s, e) =>
            {
                var r = new Rectangle(0, 0, adminStrip.Width - 1, adminStrip.Height - S(10) - 1);
                Theme.FillRound(e.Graphics, r, S(10), Theme.Soft(Theme.Amber), Theme.Blend(Theme.Amber, Color.White, 0.6f));
                TextRenderer.DrawText(e.Graphics, Theme.GWarning, Theme.Icon, new Rectangle(S(16), 0, S(24), r.Height), Theme.Amber, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Theme.Draw(e.Graphics, "Some Windows items can only be cleaned with administrator rights.", Theme.Body,
                    new Rectangle(S(46), 0, adminBtn.Left - S(56), r.Height), Theme.Text, TextFormatFlags.VerticalCenter);
            };

            Controls.Add(list);
            Controls.Add(Lay.Gap(12)); Controls[Controls.Count - 1].Dock = DockStyle.Bottom;
            Controls.Add(bottom);
            var top = Header("Free up space", "Places where Windows and apps leave files they no longer need. \"Safe to delete\" items never include your own files.");
            top.Add(adminStrip);
            Lay.Stack(this, top.ToArray());

            St.CleanChanged += Sync;
            Sync();
        }

        void Sync()
        {
            if (shown != St.CleanItems)
            {
                shown = St.CleanItems;
                list.SuspendLayout();
                foreach (Control c in list.Controls) c.Dispose();
                list.Controls.Clear();
                for (int i = shown.Count - 1; i >= 0; i--)
                {
                    var row = new CleanRow(shown[i]) { Dock = DockStyle.Top };
                    row.Changed += () => bottom.Invalidate();
                    row.ActionDone += () => { St.RefreshDrive(); St.MeasureCleanup(); };
                    list.Controls.Add(row);
                }
                list.ResumeLayout();
            }
            else
            {
                foreach (Control c in list.Controls) c.Invalidate();
            }
            adminStrip.Visible = !Program.IsAdmin() && shown.Any(i => i.Blocked);
            cleanBtn.Enabled = !busy;
            recheckBtn.Enabled = !busy && !St.Measuring;
            bottom.Invalidate();
        }

        IEnumerable<CleanItem> Selected()
        {
            return shown.Where(i => i.Checked && !i.InfoOnly && !i.Blocked);
        }

        void PaintBottom(Graphics g)
        {
            int p = S(24);
            long total = Selected().Sum(i => i.Size);
            int right = recheckBtn.Left - S(16);
            if (busy)
            {
                Theme.Draw(g, "Cleaning...", Theme.H2, new Rectangle(p, S(16), right - p, S(28)), Theme.Text);
                Theme.Draw(g, status ?? "", Theme.Small, new Rectangle(p, S(46), right - p, S(20)), Theme.Muted);
                return;
            }
            Theme.Draw(g, "SELECTED TO CLEAN", Theme.SmallBold, new Rectangle(p, S(16), right - p, S(18)), Theme.Muted);
            Theme.Draw(g, St.Measuring ? "Checking your drive..." : Fmt.Size(total), Theme.H2, new Rectangle(p, S(36), right - p, S(30)), total > 0 ? Theme.Green : Theme.Text);
        }

        void DoClean()
        {
            var sel = Selected().Where(i => i.Size > 0).ToList();
            if (sel.Count == 0)
            {
                MessageBox.Show(F, "Tick at least one item that has something to clean.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string listText = string.Join("\n", sel.Select(i => "   -  " + i.Title + "  (" + Fmt.Size(i.Size) + ")"));
            if (Ask(F, "Clean these and free about " + Fmt.Size(sel.Sum(i => i.Size)) + "?\n\n" + listText +
                "\n\nFiles that are in use right now are skipped. Your personal files are not touched.", "Clean up") != DialogResult.Yes)
                return;

            busy = true;
            Sync();
            IntPtr handle = F.Handle;
            Task.Factory.StartNew(() =>
            {
                var total = new CleanResult();
                foreach (var it in sel)
                {
                    status = "Cleaning " + it.Title + "...";
                    F.BeginInvoke(new Action(() => bottom.Invalidate()));
                    try
                    {
                        var r = CleanupCatalog.Clean(it, handle);
                        total.Freed += r.Freed; total.Deleted += r.Deleted; total.Skipped += r.Skipped;
                    }
                    catch { }
                }
                return total;
            }).ContinueWith(t => F.BeginInvoke(new Action(() =>
            {
                busy = false;
                var r = t.Exception == null ? t.Result : new CleanResult();
                St.RefreshDrive();
                St.MeasureCleanup();
                string msg = "Done! Freed " + Fmt.Size(r.Freed) + ".";
                if (r.Skipped > 0) msg += "\n\n" + Fmt.Count(r.Skipped) + " files were in use by open apps and were left alone - that's normal. Close your apps and run it again to remove more.";
                MessageBox.Show(F, msg, "Clean up finished", MessageBoxButtons.OK, MessageBoxIcon.Information);
            })));
        }
    }

    // ---------------------------------------------------------------- What's using space

    class RowData
    {
        public bool IsDir, Aggregate;
        public DirNode Node;
        public string Path, Name;
        public long Size, Files;
        public double Share;
        public Advice Adv;
    }

    class Breadcrumb : Control
    {
        public List<DirNode> Chain = new List<DirNode>();
        public string Caption;
        public event Action<DirNode> Navigate;
        readonly List<Tuple<Rectangle, DirNode>> hits = new List<Tuple<Rectangle, DirNode>>();
        int hover = -1;

        public Breadcrumb()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            hits.Clear();
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (Caption != null) { Theme.Draw(g, Caption, Theme.H2, ClientRectangle, Theme.Text, TextFormatFlags.VerticalCenter); return; }
            if (Chain.Count == 0) return;

            string sep = "  \u203A  ";
            int sepW = TextRenderer.MeasureText(g, sep, Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width;
            var names = Chain.Select(n => n.Parent == null ? n.Name.TrimEnd('\\') : n.Name).ToList();
            var widths = names.Select((n, i) => TextRenderer.MeasureText(g, n, i == names.Count - 1 ? Theme.H2 : Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width).ToList();
            int start = 0;
            Func<int> total = () => widths.Skip(start).Sum() + sepW * (names.Count - start) + (start > 0 ? sepW * 2 : 0);
            while (start < names.Count - 1 && total() > Width) start++;

            int x = 0;
            if (start > 0) { TextRenderer.DrawText(g, "..." + sep, Theme.Body, new Rectangle(x, 0, sepW * 3, Height), Theme.Faint, flags); x += TextRenderer.MeasureText(g, "..." + sep, Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width; }
            for (int i = start; i < names.Count; i++)
            {
                bool last = i == names.Count - 1;
                var r = new Rectangle(x, 0, Math.Min(widths[i] + 2, Math.Max(0, Width - x)), Height);
                int hi = hits.Count;
                Color c = last ? Theme.Text : hover == hi ? Theme.AccentDark : Theme.Accent;
                TextRenderer.DrawText(g, names[i], last ? Theme.H2 : Theme.Body, r, c, flags | TextFormatFlags.EndEllipsis);
                if (!last) hits.Add(Tuple.Create(r, Chain[i]));
                x += r.Width;
                if (!last) { TextRenderer.DrawText(g, sep, Theme.Body, new Rectangle(x, 0, sepW, Height), Theme.Faint, flags); x += sepW; }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = hits.FindIndex(t => t.Item1.Contains(e.Location));
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int h = hits.FindIndex(t => t.Item1.Contains(e.Location));
            if (h >= 0 && Navigate != null) Navigate(hits[h].Item2);
            base.OnMouseClick(e);
        }
    }

    class SpacePage : Page
    {
        readonly Panel body;
        readonly PaintBox empty, adviceStrip, actions;
        readonly FlatButton emptyScanBtn, emptyStopBtn, upBtn, foldersBtn, filesBtn, explorerBtn, openBtn, showBtn, deleteBtn;
        readonly Breadcrumb crumb;
        readonly SmoothList list;
        DirNode current;
        DriveScanner shownScan;
        bool filesMode;
        bool toldAboutBin;

        public SpacePage(MainForm f) : base(f)
        {
            // Shown before the first scan
            empty = new PaintBox { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            emptyScanBtn = new FlatButton("Scan C: drive", FlatButton.Style.Primary, Theme.GSearch) { Height = S(44) };
            emptyScanBtn.FitWidth();
            emptyStopBtn = new FlatButton("Stop", FlatButton.Style.Secondary, Theme.GStop) { Height = S(44) };
            emptyScanBtn.Click += (s, e) => St.StartScan();
            emptyStopBtn.Click += (s, e) => St.CancelScan();
            empty.Controls.Add(emptyScanBtn);
            empty.Controls.Add(emptyStopBtn);
            empty.Painting += (s, e) => PaintEmpty(e.Graphics);
            empty.Resize += (s, e) => LayoutEmpty();

            // Shown after a scan
            body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

            var toolbar = new Panel { Height = S(44), BackColor = Theme.Bg };
            upBtn = new FlatButton("Up", FlatButton.Style.Secondary, Theme.GUp);
            crumb = new Breadcrumb();
            foldersBtn = new FlatButton("Folders", FlatButton.Style.Primary, Theme.GFolder);
            filesBtn = new FlatButton("Largest files", FlatButton.Style.Secondary, Theme.GFile);
            explorerBtn = new FlatButton("Open in File Explorer", FlatButton.Style.Secondary, Theme.GOpen);
            toolbar.Controls.AddRange(new Control[] { upBtn, crumb, foldersBtn, filesBtn, explorerBtn });
            toolbar.Resize += (s, e) =>
            {
                int h = toolbar.Height;
                upBtn.Location = new Point(0, (h - upBtn.Height) / 2);
                int x = toolbar.Width;
                foreach (var b in new[] { explorerBtn, filesBtn, foldersBtn })
                {
                    x -= b.Width;
                    b.Location = new Point(x, (h - b.Height) / 2);
                    x -= b == explorerBtn ? S(18) : S(6);
                }
                crumb.Bounds = new Rectangle(upBtn.Right + S(14), 0, Math.Max(0, x - upBtn.Right - S(26)), h);
            };
            upBtn.Click += (s, e) => GoUp();
            crumb.Navigate += n => ShowNode(n);
            foldersBtn.Click += (s, e) => ShowNode(current ?? St.Scan.Root);
            filesBtn.Click += (s, e) => ShowFiles();
            explorerBtn.Click += (s, e) =>
            {
                var sel = SelectedRow();
                if (sel != null && !sel.Aggregate) Program.OpenInExplorer(sel.Path, true);
                else if (current != null) Program.OpenInExplorer(current.Path, false);
            };

            adviceStrip = new PaintBox { Height = S(56), BackColor = Theme.Bg };
            adviceStrip.Painting += (s, e) => PaintAdvice(e.Graphics);

            var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(2), S(8), S(2), S(8)) };
            list = new SmoothList { Dock = DockStyle.Fill };
            list.SetRowHeight(S(36));
            list.Columns.Add("Name", S(300));
            list.Columns.Add("Size", S(100), HorizontalAlignment.Right);
            list.Columns.Add("Share", S(150));
            list.Columns.Add("Files", S(90), HorizontalAlignment.Right);
            list.Columns.Add("What is it?", S(300));
            list.DrawSubItem += DrawCell;
            list.Resize += (s, e) => list.Stretch(4);
            list.DoubleClick += (s, e) => OpenRow(SelectedRow());
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) OpenRow(SelectedRow());
                else if (e.KeyCode == Keys.Back) GoUp();
                else if (e.KeyCode == Keys.Delete) DeleteRow(SelectedRow());
            };
            list.SelectedIndexChanged += (s, e) => UpdateActions();
            var menu = new ContextMenuStrip { Font = Theme.Body };
            menu.Items.Add("Open", null, (s, e) => OpenRow(SelectedRow()));
            menu.Items.Add("Show in File Explorer", null, (s, e) => { var r = SelectedRow(); if (r != null && !r.Aggregate) Program.OpenInExplorer(r.Path, true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Move to Recycle Bin...", null, (s, e) => DeleteRow(SelectedRow()));
            menu.Opening += (s, e) => { if (SelectedRow() == null || SelectedRow().Aggregate) e.Cancel = true; };
            list.ContextMenuStrip = menu;
            listCard.Controls.Add(list);

            actions = new PaintBox { Dock = DockStyle.Bottom, Height = S(56), BackColor = Theme.Bg };
            openBtn = new FlatButton("Open", FlatButton.Style.Secondary, Theme.GFolder);
            showBtn = new FlatButton("Show in File Explorer", FlatButton.Style.Secondary, Theme.GOpen);
            deleteBtn = new FlatButton("Move to Recycle Bin", FlatButton.Style.Danger, Theme.GDelete);
            openBtn.Click += (s, e) => OpenRow(SelectedRow());
            showBtn.Click += (s, e) => { var r = SelectedRow(); if (r != null) Program.OpenInExplorer(r.Path, true); };
            deleteBtn.Click += (s, e) => DeleteRow(SelectedRow());
            actions.Controls.AddRange(new Control[] { openBtn, showBtn, deleteBtn });
            actions.Resize += (s, e) => Lay.Arrange(actions, new Control[] { deleteBtn, showBtn, openBtn });
            actions.Painting += (s, e) => PaintActions(e.Graphics);

            body.Controls.Add(listCard);
            body.Controls.Add(actions);
            Lay.Stack(body, toolbar, Lay.Gap(10), adviceStrip, Lay.Gap(10));

            Controls.Add(body);
            Controls.Add(empty);
            var top = Header("What's using space", "Every folder on C:, biggest first, with a note on whether it's safe to remove. Double-click to look inside.");
            Lay.Stack(this, top.ToArray());

            St.ScanChanged += Sync;
            St.ScanProgress += () => { if (empty.Visible) empty.Invalidate(); };
            Sync();
        }

        void Sync()
        {
            bool has = St.Scan != null;
            body.Visible = has;
            empty.Visible = !has;
            if (has && shownScan != St.Scan)
            {
                shownScan = St.Scan;
                ShowNode(St.Scan.Root);
            }
            LayoutEmpty();
            empty.Invalidate();
        }

        public void OpenPath(string path)
        {
            if (St.Scan == null) return;
            var n = St.Scan.Find(path);
            if (n != null) ShowNode(n);
        }

        void LayoutEmpty()
        {
            bool running = St.Running != null;
            emptyScanBtn.Visible = !running;
            emptyStopBtn.Visible = running;
            int cy = empty.Height / 2 + S(70);
            emptyScanBtn.Location = new Point((empty.Width - emptyScanBtn.Width) / 2, cy);
            emptyStopBtn.Location = new Point((empty.Width - emptyStopBtn.Width) / 2, cy);
        }

        void PaintEmpty(Graphics g)
        {
            int w = Math.Min(S(620), empty.Width), h = S(330);
            var card = new Rectangle((empty.Width - w) / 2, empty.Height / 2 - S(190), w, h);
            Theme.FillRound(g, card, S(14), Color.White, Theme.Border);
            int cx = card.X + card.Width / 2;
            IconCircle(g, new Rectangle(cx - S(32), card.Y + S(30), S(64), S(64)), Theme.GPie, Theme.Accent);
            var r = St.Running;
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            var inner = new Rectangle(card.X + S(40), card.Y + S(110), card.Width - S(80), S(30));
            if (r == null)
            {
                TextRenderer.DrawText(g, "See what's taking up space", Theme.H2, inner, Theme.Text, flags);
                TextRenderer.DrawText(g, "We'll look through every folder on your C: drive and list the biggest first, with a plain-English note on whether each one is safe to remove. Nothing is deleted.",
                    Theme.Body, new Rectangle(inner.X, inner.Bottom + S(6), inner.Width, S(66)), Theme.Muted, flags);
            }
            else
            {
                string title, l1, l2;
                ScanLines(r, out title, out l1, out l2);
                TextRenderer.DrawText(g, title, Theme.H2, inner, Theme.Text, flags);
                TextRenderer.DrawText(g, l1, Theme.Body, new Rectangle(inner.X, inner.Bottom + S(4), inner.Width, S(22)), Theme.Muted, flags);
                TextRenderer.DrawText(g, l2, Theme.Small, new Rectangle(inner.X, inner.Bottom + S(28), inner.Width, S(20)), Theme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPadding);
                Theme.Bar(g, new Rectangle(inner.X, inner.Bottom + S(56), inner.Width, S(8)), St.ScanFraction, Theme.Accent);
            }
        }

        // -------- list contents

        void ShowNode(DirNode n)
        {
            if (n == null) return;
            current = n;
            filesMode = false;
            var rows = new List<RowData>();
            if (n.Children != null)
                foreach (var c in n.Children)
                {
                    string p = c.Path;
                    var adv = Advisor.For(p, true);
                    if (c.Denied) adv = new Advice(adv.Level, "Windows didn't let us look inside" + (Program.IsAdmin() ? "" : " - run as administrator to see it")) { Protected = adv.Protected };
                    rows.Add(new RowData { IsDir = true, Node = c, Path = p, Name = c.Name, Size = c.Size, Files = c.Files, Adv = adv });
                }

            bool ok;
            var files = Native.ListDir(n.Path, out ok).Where(e => !e.IsDir).OrderByDescending(e => e.Size).ToList();
            foreach (var e in files.Take(100))
            {
                string p = Native.Join(n.Path, e.Name);
                rows.Add(new RowData { Path = p, Name = e.Name, Size = e.CloudOnly ? 0 : Native.OnDiskSize(p, e.Size), Files = 1, Adv = Advisor.For(p, false) });
            }
            rows.Sort((a, b) => b.Size.CompareTo(a.Size));
            if (files.Count > 100)
            {
                var rest = files.Skip(100).ToList();
                rows.Add(new RowData { Aggregate = true, Path = n.Path, Name = Fmt.Count(rest.Count) + " smaller files", Size = rest.Sum(e => e.Size), Files = rest.Count, Adv = new Advice(Safety.Review, "Small files in this folder") });
            }
            long total = Math.Max(1, n.Size);
            foreach (var r in rows) r.Share = (double)r.Size / total;

            SetColumns(false);
            Fill(rows);
            crumb.Caption = null;
            crumb.Chain.Clear();
            for (var a = n; a != null; a = a.Parent) crumb.Chain.Insert(0, a);
            crumb.Invalidate();
            upBtn.Enabled = n.Parent != null;
            SetMode();
        }

        void ShowFiles()
        {
            if (St.Scan == null) return;
            filesMode = true;
            long total = Math.Max(1, St.Scan.Root.Size);
            var rows = St.Scan.LargestFiles.Select(fi => new RowData
            {
                Path = fi.Path, Name = Path.GetFileName(fi.Path), Size = fi.Size, Files = 1,
                Share = (double)fi.Size / total, Adv = Advisor.For(fi.Path, false)
            }).ToList();
            SetColumns(true);
            Fill(rows);
            crumb.Caption = "The largest files on your C: drive";
            crumb.Invalidate();
            upBtn.Enabled = false;
            SetMode();
        }

        void SetMode()
        {
            foldersBtn.Kind = filesMode ? FlatButton.Style.Secondary : FlatButton.Style.Primary;
            filesBtn.Kind = filesMode ? FlatButton.Style.Primary : FlatButton.Style.Secondary;
            foldersBtn.Invalidate(); filesBtn.Invalidate();
            adviceStrip.Invalidate();
            UpdateActions();
        }

        void SetColumns(bool files)
        {
            list.BeginUpdate();
            list.Columns[0].Width = S(files ? 230 : 300);
            list.Columns[2].Width = S(files ? 110 : 150);
            list.Columns[3].Text = files ? "Location" : "Files";
            list.Columns[3].TextAlign = files ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            list.Columns[3].Width = S(files ? 220 : 90);
            list.Stretch(4);
            list.EndUpdate();
        }

        void Fill(List<RowData> rows)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var r in rows)
                list.Items.Add(new ListViewItem(new[] { r.Name, "", "", "", "" }) { Tag = r });
            list.EndUpdate();
            if (list.Items.Count > 0) list.Items[0].Selected = true;
            UpdateActions();
        }

        RowData SelectedRow()
        {
            return list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as RowData : null;
        }

        void DrawCell(object sender, DrawListViewSubItemEventArgs e)
        {
            var row = e.Item.Tag as RowData;
            list.RowBackground(e);
            if (row == null) return;
            var g = e.Graphics;
            var r = SmoothList.Inner(e.Bounds);
            var vc = TextFormatFlags.VerticalCenter;
            switch (e.ColumnIndex)
            {
                case 0:
                    string glyph = row.IsDir ? Theme.GFolder : Theme.GFile;
                    Color gc = row.IsDir ? Theme.Amber : row.Aggregate ? Theme.Faint : Theme.Muted;
                    TextRenderer.DrawText(g, glyph, Theme.Icon, new Rectangle(r.X, r.Y, S(22), r.Height), gc, vc | TextFormatFlags.NoPadding);
                    Theme.Draw(g, row.Name, row.IsDir ? Theme.BodyBold : Theme.Body, new Rectangle(r.X + S(28), r.Y, r.Width - S(28), r.Height), row.Aggregate ? Theme.Muted : Theme.Text, vc);
                    break;
                case 1:
                    Theme.Draw(g, Fmt.Size(row.Size), Theme.BodyBold, r, Theme.Text, vc | TextFormatFlags.Right);
                    break;
                case 2:
                    int pctW = S(44);
                    Theme.Bar(g, new Rectangle(r.X, r.Y + r.Height / 2 - S(4), Math.Max(S(10), r.Width - pctW - S(6)), S(8)), row.Share, Theme.Accent);
                    string pct = row.Share >= 0.01 ? Math.Round(row.Share * 100) + "%" : "<1%";
                    Theme.Draw(g, pct, Theme.Small, new Rectangle(r.Right - pctW, r.Y, pctW, r.Height), Theme.Muted, vc | TextFormatFlags.Right);
                    break;
                case 3:
                    if (filesMode) Theme.Draw(g, Path.GetDirectoryName(row.Path), Theme.Small, r, Theme.Muted, vc | TextFormatFlags.PathEllipsis);
                    else Theme.Draw(g, row.IsDir || row.Aggregate ? Fmt.Count(row.Files) : "", Theme.Small, r, Theme.Muted, vc | TextFormatFlags.Right);
                    break;
                case 4:
                    var c = Theme.ForLevel(row.Adv.Level);
                    int d = S(8);
                    Theme.FillRound(g, new Rectangle(r.X, r.Y + (r.Height - d) / 2, d, d), d / 2, c, null);
                    Theme.Draw(g, row.Adv.Text, Theme.Small, new Rectangle(r.X + S(16), r.Y, r.Width - S(16), r.Height), Theme.Muted, vc);
                    break;
            }
        }

        void PaintAdvice(Graphics g)
        {
            var card = new Rectangle(0, 0, adviceStrip.Width - 1, adviceStrip.Height - 1);
            Theme.FillRound(g, card, S(10), Color.White, Theme.Border);
            int p = S(16), pw;
            if (filesMode || current == null)
            {
                TextRenderer.DrawText(g, Theme.GInfo, Theme.Icon, new Rectangle(p, 0, S(20), card.Height), Theme.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Theme.Draw(g, "Biggest single files found by the scan. Select one and use \"Show in File Explorer\" to see where it is.", Theme.Body,
                    new Rectangle(p + S(30), 0, card.Width - p * 2 - S(30), card.Height), Theme.Text, TextFormatFlags.VerticalCenter);
                return;
            }
            var adv = Advisor.For(current.Path, true);
            int ph = TextRenderer.MeasureText(g, "X", Theme.SmallBold).Height + S(6);
            Theme.Pill(g, new Point(p, (card.Height - ph) / 2), Theme.LevelName(adv.Level), Theme.ForLevel(adv.Level), out pw);
            string right = Fmt.Size(current.Size) + "  -  " + Fmt.Count(current.Files) + " files";
            int rw = TextRenderer.MeasureText(g, right, Theme.BodyBold).Width + S(4);
            Theme.Draw(g, right, Theme.BodyBold, new Rectangle(card.Width - p - rw, 0, rw, card.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            string text = current.Denied ? "Windows didn't let us look inside this folder." + (Program.IsAdmin() ? "" : " Run as administrator to see it.") : adv.Text;
            Theme.Draw(g, text, Theme.Body, new Rectangle(p + pw + S(12), 0, card.Width - p - pw - S(24) - rw - p, card.Height), Theme.Text, TextFormatFlags.VerticalCenter);
        }

        void PaintActions(Graphics g)
        {
            var row = SelectedRow();
            int right = openBtn.Visible ? openBtn.Left - S(16) : actions.Width;
            string text = row == null ? "Select a folder or file to see what you can do with it." :
                row.Aggregate ? "These are many small files - open the folder in File Explorer to review them." :
                row.Name + "  -  " + Fmt.Size(row.Size) + "  -  " + Theme.LevelName(row.Adv.Level);
            Theme.Draw(g, text, Theme.Body, new Rectangle(S(4), 0, right - S(4), actions.Height), Theme.Muted, TextFormatFlags.VerticalCenter);
        }

        void UpdateActions()
        {
            var row = SelectedRow();
            bool has = row != null && !row.Aggregate;
            openBtn.Enabled = has;
            showBtn.Enabled = has;
            deleteBtn.Enabled = has && !row.Adv.Protected && row.Adv.Level != Safety.System;
            openBtn.Text = has && row.IsDir ? "Look inside" : "Open";
            openBtn.FitWidth();
            Lay.Arrange(actions, new Control[] { deleteBtn, showBtn, openBtn });
            actions.Invalidate();
        }

        void GoUp()
        {
            if (filesMode) { ShowNode(current ?? St.Scan.Root); return; }
            if (current != null && current.Parent != null)
            {
                var from = current;
                ShowNode(current.Parent);
                foreach (ListViewItem it in list.Items)
                    if (((RowData)it.Tag).Node == from) { it.Selected = true; it.Focused = true; it.EnsureVisible(); }
            }
        }

        void OpenRow(RowData row)
        {
            if (row == null) return;
            if (row.IsDir && row.Node != null) ShowNode(row.Node);
            else if (row.Aggregate) Program.OpenInExplorer(row.Path, false);
            else
            {
                if (filesMode) { Program.OpenInExplorer(row.Path, true); return; }
                Program.OpenInExplorer(row.Path, true);
            }
        }

        void DeleteRow(RowData row)
        {
            if (row == null || row.Aggregate) return;
            var adv = row.Adv;
            if (adv.Level == Safety.System || adv.Protected)
            {
                string why = adv.Level == Safety.System ? "\n\nRemoving it could stop Windows from working properly." :
                    adv.Level == Safety.App ? "\n\nTo remove a program safely, uninstall it from the Installed apps page." :
                    "\n\nOpen it and delete individual items inside instead.";
                MessageBox.Show(F, row.Name + "\n\n" + adv.Text + "." + why, "This one is protected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string msg = "Move this to the Recycle Bin?\n\n" + row.Path + "\nSize: " + Fmt.Size(row.Size) + "\n\n" + adv.Text + ".";
            if (adv.Level != Safety.Safe) msg += "\n\nMake sure you don't need it. You can still restore it from the Recycle Bin.";
            if (Ask(F, msg, "Move to Recycle Bin", adv.Level == Safety.Safe ? MessageBoxIcon.Question : MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                if (row.IsDir) FileSystem.DeleteDirectory(row.Path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
                else FileSystem.DeleteFile(row.Path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show(F, "Couldn't move it to the Recycle Bin:\n\n" + ex.Message + "\n\nIt may be in use by an open app.", "Not deleted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (row.IsDir ? !Directory.Exists(row.Path) : !File.Exists(row.Path))
            {
                if (row.IsDir) St.Scan.Forget(row.Node);
                else St.Scan.ForgetFile(row.Path, row.Size);
                if (filesMode) ShowFiles(); else ShowNode(current);
                St.RefreshDrive();
                St.MeasureCleanup();
                if (!toldAboutBin)
                {
                    toldAboutBin = true;
                    MessageBox.Show(F, "Moved to the Recycle Bin.\n\nThe space is freed once you empty the Recycle Bin - you can do that on the Clean up page.",
                        "Moved to Recycle Bin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }

    // ---------------------------------------------------------------- Installed apps

    class AppsPage : Page
    {
        readonly TextBox search;
        readonly CheckBox onlyC;
        readonly SmoothList list;
        readonly PaintBox status;
        readonly FlatButton openBtn, uninstallBtn;

        public AppsPage(MainForm f) : base(f)
        {
            var toolbar = new Panel { Height = S(44), BackColor = Theme.Bg };
            var searchBox = new PaintBox { Width = S(320), Height = S(40), BackColor = Theme.Bg };
            search = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Body, BackColor = Color.White };
            searchBox.Controls.Add(search);
            searchBox.Resize += (s, e) => search.Bounds = new Rectangle(S(40), (searchBox.Height - search.PreferredHeight) / 2, searchBox.Width - S(52), search.PreferredHeight);
            searchBox.Painting += (s, e) =>
            {
                Theme.FillRound(e.Graphics, new Rectangle(0, 0, searchBox.Width - 1, searchBox.Height - 1), S(8), Color.White, search.Focused ? Theme.Accent : Theme.Border);
                TextRenderer.DrawText(e.Graphics, Theme.GSearch, Theme.Icon, new Rectangle(S(14), 0, S(20), searchBox.Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            search.GotFocus += (s, e) => searchBox.Invalidate();
            search.LostFocus += (s, e) => searchBox.Invalidate();
            search.HandleCreated += (s, e) => Native.SendMessageW(search.Handle, 0x1501, (IntPtr)1, "Search apps");
            search.TextChanged += (s, e) => Populate();
            search.Bounds = new Rectangle(S(40), (searchBox.Height - search.PreferredHeight) / 2, searchBox.Width - S(52), search.PreferredHeight);

            onlyC = new CheckBox { Text = "Only apps on C:", AutoSize = true, Checked = true, Font = Theme.Body, BackColor = Theme.Bg, ForeColor = Theme.Text, Cursor = Cursors.Hand };
            onlyC.CheckedChanged += (s, e) => Populate();
            var refreshBtn = new FlatButton("Refresh", FlatButton.Style.Secondary, Theme.GRefresh);
            var settingsBtn = new FlatButton("Windows app settings", FlatButton.Style.Secondary, Theme.GSettings);
            refreshBtn.Click += (s, e) => St.LoadApps();
            settingsBtn.Click += (s, e) => Program.Shell("ms-settings:appsfeatures");
            toolbar.Controls.AddRange(new Control[] { searchBox, onlyC, refreshBtn, settingsBtn });
            toolbar.Resize += (s, e) =>
            {
                searchBox.Location = new Point(0, (toolbar.Height - searchBox.Height) / 2);
                onlyC.Location = new Point(searchBox.Right + S(18), (toolbar.Height - onlyC.Height) / 2);
                Lay.Arrange(toolbar, new Control[] { settingsBtn, refreshBtn });
            };

            var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(2), S(8), S(2), S(8)) };
            list = new SmoothList { Dock = DockStyle.Fill };
            list.SetRowHeight(S(50));
            list.Columns.Add("App", S(380));
            list.Columns.Add("Size", S(110), HorizontalAlignment.Right);
            list.Columns.Add("Installed", S(130));
            list.Columns.Add("Location", S(300));
            list.DrawSubItem += DrawCell;
            list.Resize += (s, e) => list.Stretch(3);
            list.SelectedIndexChanged += (s, e) => UpdateActions();
            list.DoubleClick += (s, e) => OpenFolder();
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) Uninstall(); };
            listCard.Controls.Add(list);

            status = new PaintBox { Dock = DockStyle.Bottom, Height = S(56), BackColor = Theme.Bg };
            openBtn = new FlatButton("Open install folder", FlatButton.Style.Secondary, Theme.GOpen);
            uninstallBtn = new FlatButton("Uninstall...", FlatButton.Style.Danger, Theme.GDelete);
            openBtn.Click += (s, e) => OpenFolder();
            uninstallBtn.Click += (s, e) => Uninstall();
            status.Controls.AddRange(new Control[] { openBtn, uninstallBtn });
            status.Resize += (s, e) => Lay.Arrange(status, new Control[] { uninstallBtn, openBtn });
            status.Painting += (s, e) => PaintStatus(e.Graphics);

            Controls.Add(listCard);
            Controls.Add(status);
            var top = Header("Installed apps", "Biggest first. Remove apps you no longer use - Windows runs each app's own uninstaller.");
            top.Add(toolbar);
            top.Add(Lay.Gap(10));
            Lay.Stack(this, top.ToArray());

            St.AppsChanged += Populate;
            Populate();
        }

        List<AppInfo> visible = new List<AppInfo>();

        void Populate()
        {
            string q = search.Text.Trim();
            visible = St.Apps.Where(a => (!onlyC.Checked || a.OnDriveC) &&
                (q.Length == 0 || a.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || a.Publisher.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var a in visible) list.Items.Add(new ListViewItem(new[] { a.Name, "", "", "" }) { Tag = a });
            list.EndUpdate();
            UpdateActions();
        }

        AppInfo Selected() { return list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as AppInfo : null; }

        void UpdateActions()
        {
            var a = Selected();
            uninstallBtn.Enabled = a != null;
            openBtn.Enabled = a != null && a.Location != null && Directory.Exists(a.Location);
            status.Invalidate();
        }

        void DrawCell(object sender, DrawListViewSubItemEventArgs e)
        {
            var a = e.Item.Tag as AppInfo;
            list.RowBackground(e);
            if (a == null) return;
            var g = e.Graphics;
            var r = SmoothList.Inner(e.Bounds);
            var vc = TextFormatFlags.VerticalCenter;
            switch (e.ColumnIndex)
            {
                case 0:
                    Theme.Draw(g, a.Name, Theme.BodyBold, new Rectangle(r.X, r.Y + S(7), r.Width, S(20)), Theme.Text);
                    string sub = a.Publisher.Length > 0 ? a.Publisher : "";
                    if (a.Version.Length > 0) sub += (sub.Length > 0 ? "  -  " : "") + "Version " + a.Version;
                    Theme.Draw(g, sub, Theme.Small, new Rectangle(r.X, r.Y + S(27), r.Width, S(18)), Theme.Muted);
                    break;
                case 1:
                    Theme.Draw(g, a.Size > 0 ? Fmt.Size(a.Size) : "-", Theme.BodyBold, r, a.Size > 0 ? Theme.Text : Theme.Faint, vc | TextFormatFlags.Right);
                    break;
                case 2:
                    Theme.Draw(g, a.Installed > DateTime.MinValue ? a.Installed.ToString("d MMM yyyy") : "-", Theme.Small, r, Theme.Muted, vc);
                    break;
                case 3:
                    Theme.Draw(g, a.Location ?? "Unknown location", Theme.Small, r, a.Location == null ? Theme.Faint : Theme.Muted, vc | TextFormatFlags.PathEllipsis);
                    break;
            }
        }

        void PaintStatus(Graphics g)
        {
            string text;
            if (St.AppsLoading && St.Apps.Count == 0) text = "Loading installed apps...";
            else
            {
                text = visible.Count + " apps.";
                text += St.Scan == null ? "  Sizes are what each app reports; a drive scan makes them more accurate." : "  Sizes include what the drive scan measured.";
            }
            int right = openBtn.Left - S(16);
            Theme.Draw(g, text, Theme.Small, new Rectangle(S(4), 0, right - S(4), status.Height), Theme.Muted, TextFormatFlags.VerticalCenter);
        }

        void OpenFolder()
        {
            var a = Selected();
            if (a != null && a.Location != null && Directory.Exists(a.Location)) Program.OpenInExplorer(a.Location, false);
        }

        void Uninstall()
        {
            var a = Selected();
            if (a == null) return;
            if (Ask(F, "Uninstall " + a.Name + "?\n\nWindows will open this app's own uninstaller - follow its steps to finish. " +
                "This list refreshes when it's done.", "Uninstall app") != DialogResult.Yes) return;
            try
            {
                var proc = InstalledApps.RunUninstaller(a);
                if (proc != null)
                {
                    proc.EnableRaisingEvents = true;
                    proc.Exited += (s, e) => F.BeginInvoke(new Action(() => { St.LoadApps(); St.RefreshDrive(); }));
                }
            }
            catch (Exception ex)
            {
                var w = ex as Win32Exception;
                if (w != null && w.NativeErrorCode == 1223) return; // user said No to the permission prompt
                MessageBox.Show(F, "Couldn't start the uninstaller for " + a.Name + ".\n\nTry removing it from Windows Settings > Apps instead.",
                    "Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
