using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CDriveCleaner
{
    // Everything the pages share: the drive scan, the clean-up list and the apps list.
    class AppState
    {
        public MainForm Form;
        public DriveScanner Scan;     // last finished scan
        public DriveScanner Running;  // scan in progress
        public List<CleanItem> CleanItems = new List<CleanItem>();
        public bool Measuring;
        public List<AppInfo> Apps = new List<AppInfo>();
        public bool AppsLoading;
        public long DriveTotal, DriveFree;
        public readonly string Drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        public event Action ScanChanged, ScanProgress, CleanChanged, AppsChanged, DriveChanged;
        readonly Timer timer = new Timer { Interval = 200 };

        public AppState(MainForm form)
        {
            Form = form;
            timer.Tick += (s, e) => Fire(ScanProgress);
        }

        void Fire(Action a) { if (a != null) a(); }
        void Ui(Action a) { if (!Form.IsDisposed) Form.BeginInvoke(a); }

        public void RefreshDrive()
        {
            try
            {
                var d = new DriveInfo(Drive);
                DriveTotal = d.TotalSize;
                DriveFree = d.TotalFreeSpace;
            }
            catch { }
            Fire(DriveChanged);
        }

        public double ScanFraction
        {
            get
            {
                var r = Running;
                long used = DriveTotal - DriveFree;
                if (r == null || used <= 0) return 0;
                return Math.Min(0.99, (double)System.Threading.Interlocked.Read(ref r.BytesSeen) / used);
            }
        }

        public void StartScan()
        {
            if (Running != null) return;
            RefreshDrive();
            var sc = new DriveScanner(Drive);
            Running = sc;
            Fire(ScanChanged);
            timer.Start();
            Task.Factory.StartNew(() =>
            {
                try { sc.Run(); } catch (Exception ex) { sc.Error = ex.Message; }
            }, TaskCreationOptions.LongRunning).ContinueWith(t => Ui(() =>
            {
                timer.Stop();
                Running = null;
                if (!sc.CancelRequested && sc.Error == null)
                {
                    Scan = sc;
                    InstalledApps.ApplyScanSizes(Apps, sc);
                    Fire(AppsChanged);
                }
                Fire(ScanChanged);
            }));
        }

        public void CancelScan()
        {
            if (Running != null) Running.CancelRequested = true;
        }

        public void MeasureCleanup()
        {
            if (Measuring) return;
            Measuring = true;
            var items = CleanupCatalog.Build();
            CleanItems = items;
            Fire(CleanChanged);
            Task.Factory.StartNew(() =>
            {
                foreach (var it in items)
                {
                    try { CleanupCatalog.Measure(it); } catch { it.Measured = true; }
                    Ui(() => Fire(CleanChanged));
                }
            }).ContinueWith(t => Ui(() => { Measuring = false; Fire(CleanChanged); }));
        }

        public void LoadApps()
        {
            if (AppsLoading) return;
            AppsLoading = true;
            Fire(AppsChanged);
            Task.Factory.StartNew(() => InstalledApps.Load()).ContinueWith(t => Ui(() =>
            {
                AppsLoading = false;
                if (t.Exception == null)
                {
                    Apps = t.Result;
                    InstalledApps.ApplyScanSizes(Apps, Scan);
                }
                Fire(AppsChanged);
            }));
        }

        public long SafeToClean
        {
            get
            {
                long s = 0;
                foreach (var it in CleanItems) if (!it.InfoOnly && it.Level == Safety.Safe) s += it.Size;
                return s;
            }
        }
    }

    class MainForm : Form
    {
        public readonly AppState State;
        readonly Panel host;
        readonly List<NavButton> nav = new List<NavButton>();
        readonly List<Control> pages = new List<Control>();
        public HomePage Home;
        public CleanPage Clean;
        public SpacePage Space;
        public AppsPage AppsView;

        public MainForm()
        {
            State = new AppState(this);
            Text = "C Drive Cleaner" + (Program.IsAdmin() ? "  (Administrator)" : "");
            Font = Theme.Body;
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None;
            Size = new Size(Theme.S(1200), Theme.S(800));
            MinimumSize = new Size(Theme.S(1000), Theme.S(680));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Theme.AppIcon; } catch { }

            host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Controls.Add(host);
            var sidebar = BuildSidebar();
            Controls.Add(sidebar);

            Home = new HomePage(this);
            Clean = new CleanPage(this);
            Space = new SpacePage(this);
            AppsView = new AppsPage(this);
            foreach (Control p in new Control[] { Home, Clean, Space, AppsView })
            {
                p.Dock = DockStyle.Fill;
                p.Visible = false;
                host.Controls.Add(p);
                pages.Add(p);
            }
            Go(0);

            Shown += (s, e) =>
            {
                State.RefreshDrive();
                State.MeasureCleanup();
                State.LoadApps();
            };
            FormClosing += (s, e) => State.CancelScan();
        }

        Control BuildSidebar()
        {
            var side = new PaintBox { Dock = DockStyle.Left, Width = Theme.S(240), BackColor = Theme.Sidebar };
            side.Painting += (s, e) =>
            {
                var g = e.Graphics;
                var logo = new Rectangle(Theme.S(24), Theme.S(26), Theme.S(36), Theme.S(36));
                g.DrawIcon(Theme.AppIcon, logo);
                TextRenderer.DrawText(g, "C Drive Cleaner", Theme.H2, new Point(Theme.S(70), Theme.S(24)), Color.White);
                TextRenderer.DrawText(g, "Free up space safely", Theme.Small, new Point(Theme.S(71), Theme.S(48)), Theme.SidebarText);
            };

            string[] names = { "Home", "Clean up", "What's using space", "Installed apps" };
            string[] glyphs = { Theme.GHome, Theme.GBroom, Theme.GPie, Theme.GApps };
            int y = Theme.S(96);
            for (int i = 0; i < names.Length; i++)
            {
                var b = new NavButton(names[i], glyphs[i]) { Width = side.Width, Location = new Point(0, y) };
                int idx = i;
                b.Click += (s, e) => Go(idx);
                side.Controls.Add(b);
                nav.Add(b);
                y += b.Height + Theme.S(2);
            }

            // Bottom: admin status
            var adminBox = new PaintBox { Dock = DockStyle.Bottom, Height = Theme.S(Program.IsAdmin() ? 70 : 130), BackColor = Theme.Sidebar };
            adminBox.Painting += (s, e) =>
            {
                var g = e.Graphics;
                int x = Theme.S(24);
                if (Program.IsAdmin())
                {
                    TextRenderer.DrawText(g, Theme.GShield, Theme.Icon, new Point(x, Theme.S(22)), Color.FromArgb(74, 222, 128));
                    TextRenderer.DrawText(g, "Running as administrator", Theme.Small, new Point(x + Theme.S(26), Theme.S(22)), Theme.SidebarText);
                }
                else
                {
                    var r = new Rectangle(x, Theme.S(8), adminBox.Width - x * 2, Theme.S(44));
                    TextRenderer.DrawText(g, "Some clean-up items and folders need administrator rights.", Theme.Small, r, Theme.SidebarText, TextFormatFlags.WordBreak);
                }
            };
            if (!Program.IsAdmin())
            {
                var btn = new FlatButton("Run as administrator", FlatButton.Style.Secondary, Theme.GShield);
                btn.Location = new Point(Theme.S(24), Theme.S(60));
                btn.Width = side.Width - Theme.S(48);
                btn.Click += (s, e) => AskRestartAsAdmin();
                adminBox.Controls.Add(btn);
            }
            side.Controls.Add(adminBox);
            return side;
        }

        public void AskRestartAsAdmin()
        {
            if (State.Running != null &&
                MessageBox.Show(this, "A scan is running. Restarting will stop it. Continue?", "Run as administrator", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            if (Program.RestartAsAdmin()) Close();
        }

        public void Go(int index)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                nav[i].Selected = i == index;
                nav[i].Invalidate();
            }
            pages[index].BringToFront();
        }

        public void ShowFolder(string path)
        {
            Go(2);
            Space.OpenPath(path);
        }
    }
}
