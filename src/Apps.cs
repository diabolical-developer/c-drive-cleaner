using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CDriveCleaner
{
    class AppInfo
    {
        public string Name, Publisher, Version, Location, Uninstall;
        public long Size;
        public bool SizeFromScan;
        public DateTime Installed;

        public bool OnDriveC
        {
            get { return Location == null || Location.StartsWith("C:", StringComparison.OrdinalIgnoreCase); }
        }
    }

    // Reads the same list Windows shows in Settings > Apps > Installed apps.
    static class InstalledApps
    {
        const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public static List<AppInfo> Load()
        {
            var result = new List<AppInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Read(RegistryHive.LocalMachine, RegistryView.Registry64, result, seen);
            Read(RegistryHive.LocalMachine, RegistryView.Registry32, result, seen);
            Read(RegistryHive.CurrentUser, RegistryView.Registry64, result, seen);
            Read(RegistryHive.CurrentUser, RegistryView.Registry32, result, seen);
            result.Sort((a, b) => b.Size != a.Size ? b.Size.CompareTo(a.Size) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        static void Read(RegistryHive hive, RegistryView view, List<AppInfo> result, HashSet<string> seen)
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(hive, view))
                using (var k = bk.OpenSubKey(Key))
                {
                    if (k == null) return;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using (var a = k.OpenSubKey(sub))
                            {
                                var app = FromKey(a);
                                if (app != null && seen.Add(app.Name + "|" + app.Version)) result.Add(app);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        static AppInfo FromKey(RegistryKey a)
        {
            if (a == null) return null;
            string name = a.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(name)) return null;
            object sc = a.GetValue("SystemComponent");
            if (sc is int && (int)sc == 1) return null;
            if (a.GetValue("ParentKeyName") != null) return null;
            string rt = a.GetValue("ReleaseType") as string;
            if (rt != null && (rt.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0 || rt == "Hotfix")) return null;
            string un = a.GetValue("UninstallString") as string;
            if (string.IsNullOrWhiteSpace(un)) return null;

            var app = new AppInfo();
            app.Name = name.Trim();
            app.Publisher = (a.GetValue("Publisher") as string ?? "").Trim();
            app.Version = (a.GetValue("DisplayVersion") as string ?? "").Trim();
            app.Uninstall = un.Trim();
            object es = a.GetValue("EstimatedSize");
            if (es is int) app.Size = (long)unchecked((uint)(int)es) * 1024L;
            string date = a.GetValue("InstallDate") as string;
            DateTime d;
            if (date != null && DateTime.TryParseExact(date.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                app.Installed = d;
            app.Location = CleanPath(a.GetValue("InstallLocation") as string);
            if (app.Location == null) app.Location = FromIcon(a.GetValue("DisplayIcon") as string);
            return app;
        }

        static string CleanPath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            p = Environment.ExpandEnvironmentVariables(p.Trim().Trim('"').Trim()).Replace('/', '\\');
            if (p.Length < 3 || p[1] != ':') return null;
            return p.TrimEnd('\\');
        }

        static string FromIcon(string icon)
        {
            if (string.IsNullOrWhiteSpace(icon)) return null;
            string p = icon.Trim();
            int comma = p.LastIndexOf(',');
            if (comma > 2 && !p.Substring(comma).Contains("\\")) p = p.Substring(0, comma);
            p = CleanPath(p);
            if (p == null) return null;
            try
            {
                string dir = Path.GetDirectoryName(p);
                if (dir == null) return null;
                string low = dir.ToLowerInvariant();
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
                if (low.StartsWith(win) || low.Contains("\\installer") || low.Contains("\\package cache")) return null;
                return dir;
            }
            catch { return null; }
        }

        // After a drive scan we know the real size of each app's folder, which is often better than what the app reported.
        public static void ApplyScanSizes(List<AppInfo> apps, DriveScanner scan)
        {
            if (scan == null || scan.Root == null) return;
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData })
                roots.Add(Environment.GetFolderPath(f).TrimEnd('\\'));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));

            foreach (var a in apps)
            {
                if (a.Location == null || roots.Contains(a.Location) || a.Location.Length <= 3) continue;
                var node = scan.Find(a.Location);
                if (node != null && node.Size > 0) { a.Size = node.Size; a.SizeFromScan = true; }
            }
            apps.Sort((x, y) => y.Size.CompareTo(x.Size));
        }

        // Starts the app's own uninstaller (the same thing Windows Settings does).
        public static Process RunUninstaller(AppInfo app)
        {
            string cmd = app.Uninstall.Trim();
            string exe, args;
            if (cmd.StartsWith("\""))
            {
                int q = cmd.IndexOf('"', 1);
                if (q < 0) { exe = cmd.Trim('"'); args = ""; }
                else { exe = cmd.Substring(1, q - 1); args = cmd.Substring(q + 1).Trim(); }
            }
            else
            {
                int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i >= 0) { exe = cmd.Substring(0, i + 4); args = cmd.Substring(i + 4).Trim(); }
                else { exe = cmd; args = ""; }
            }
            if (Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
                args = Regex.Replace(args, @"/I\s*\{", "/X{", RegexOptions.IgnoreCase);

            var psi = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(exe), args);
            psi.UseShellExecute = true;
            return Process.Start(psi);
        }
    }
}
