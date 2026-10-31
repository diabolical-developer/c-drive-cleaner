using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CDriveCleaner
{
    class CleanItem
    {
        public string Title, Description;
        public Safety Level;
        public List<string> Folders = new List<string>();   // contents are removed, the folder itself stays
        public List<string> Patterns = new List<string>();  // "folder|pattern" for single files
        public bool IsRecycleBin, InfoOnly, NeedsAdmin, Checked, Blocked;
        public int MinAgeHours;
        public string ActionText;
        public Action Action;
        public string OpenPath;
        public long Size, Count;
        public bool Measured;
    }

    class CleanResult
    {
        public long Freed, Deleted, Skipped;
    }

    // The list of places Windows and apps leave files that nobody needs.
    static class CleanupCatalog
    {
        static string Env(Environment.SpecialFolder f) { return Environment.GetFolderPath(f); }

        public static List<CleanItem> Build()
        {
            string local = Env(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Env(Environment.SpecialFolder.ApplicationData);
            string win = Env(Environment.SpecialFolder.Windows);
            string pd = Env(Environment.SpecialFolder.CommonApplicationData);
            string user = Env(Environment.SpecialFolder.UserProfile);
            string drive = Path.GetPathRoot(win);
            string temp = Path.GetTempPath().TrimEnd('\\');
            bool admin = Program.IsAdmin();
            var list = new List<CleanItem>();

            var rb = Item("Recycle Bin", "Files you deleted earlier. They still use space until the Recycle Bin is emptied.", Safety.Safe, true);
            rb.IsRecycleBin = true;
            list.Add(rb);

            var t = Item("Temporary files", "Leftovers apps create while they work. Anything used in the last 24 hours is kept.", Safety.Safe, true);
            t.Folders.Add(temp); t.MinAgeHours = 24; t.OpenPath = temp;
            Add(list, t);

            var wt = Item("Windows temporary files", "Temporary files made by Windows and installers. Recent ones are kept.", Safety.Safe, true);
            wt.Folders.Add(Path.Combine(win, "Temp")); wt.MinAgeHours = 24; wt.NeedsAdmin = true;
            Add(list, wt);

            var wu = Item("Windows Update leftovers", "Update files Windows has already installed. Windows downloads them again if it ever needs them.", Safety.Safe, true);
            wu.Folders.Add(Path.Combine(win, @"SoftwareDistribution\Download")); wu.MinAgeHours = 24; wu.NeedsAdmin = true;
            Add(list, wu);

            var dop = Item("Delivery Optimization files", "Pieces of updates Windows shares with other PCs. Safe to remove.", Safety.Safe, true);
            dop.Folders.Add(Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")); dop.NeedsAdmin = true;
            Add(list, dop);

            var cr = Item("Crash reports & error logs", "Reports saved when an app or Windows crashed. Only useful to developers.", Safety.Safe, true);
            cr.Folders.Add(Path.Combine(local, "CrashDumps"));
            cr.Folders.Add(Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive"));
            cr.Folders.Add(Path.Combine(pd, @"Microsoft\Windows\WER\ReportArchive"));
            cr.Folders.Add(Path.Combine(pd, @"Microsoft\Windows\WER\ReportQueue"));
            cr.Folders.Add(Path.Combine(win, "Minidump"));
            cr.Folders.Add(Path.Combine(win, "LiveKernelReports"));
            cr.Patterns.Add(win + "|MEMORY.DMP");
            Add(list, cr);

            var th = Item("Thumbnail previews", "Small preview pictures of your photos and videos. Windows makes them again when needed.", Safety.Safe, true);
            th.Patterns.Add(Path.Combine(local, @"Microsoft\Windows\Explorer") + "|thumbcache_*.db");
            th.OpenPath = Path.Combine(local, @"Microsoft\Windows\Explorer");
            Add(list, th);

            AddChromium(list, "Google Chrome", Path.Combine(local, @"Google\Chrome\User Data"));
            AddChromium(list, "Microsoft Edge", Path.Combine(local, @"Microsoft\Edge\User Data"));
            AddChromium(list, "Brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"));
            string ff = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ff))
            {
                var fi = Item("Firefox cache", BrowserText, Safety.Safe, true);
                foreach (var prof in SafeDirs(ff)) fi.Folders.Add(Path.Combine(prof, "cache2"));
                fi.OpenPath = ff;
                Add(list, fi);
            }

            var ac = Item("App caches", "Cached files from apps like VS Code, Discord, Slack, Teams and Spotify. Close those apps first for the best result.", Safety.Safe, true);
            foreach (var app in new[] { "Code", "discord", "Slack", @"Microsoft\Teams" })
                foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", "CachedData", "CachedExtensionVSIXs" })
                    ac.Folders.Add(Path.Combine(roaming, app, sub));
            ac.Folders.Add(Path.Combine(local, @"Spotify\Data"));
            Add(list, ac);

            var dev = Item("Developer package caches", "Packages kept by npm, pip, Yarn, NuGet and Gradle. They download again automatically, but your next build may take longer.", Safety.Review, false);
            dev.Folders.Add(Path.Combine(local, "npm-cache"));
            dev.Folders.Add(Path.Combine(local, @"pip\cache"));
            dev.Folders.Add(Path.Combine(local, @"Yarn\Cache"));
            dev.Folders.Add(Path.Combine(local, @"NuGet\v3-cache"));
            dev.Folders.Add(Path.Combine(user, @".nuget\packages"));
            dev.Folders.Add(Path.Combine(user, @".gradle\caches"));
            Add(list, dev);

            // Things we only point out - the user decides.
            string dl = Path.Combine(user, "Downloads");
            if (Directory.Exists(dl))
            {
                var d = Item("Your Downloads folder", "Old installers and zip files often pile up here. We never delete your personal files - open it and remove what you don't need.", Safety.Personal, false);
                d.InfoOnly = true; d.Folders.Add(dl); d.OpenPath = dl;
                d.ActionText = "Open folder";
                d.Action = () => Program.OpenInExplorer(dl, false);
                list.Add(d);
            }

            string hib = Path.Combine(drive, "hiberfil.sys");
            Entry he;
            if (Native.TryGetFile(hib, out he) && he.Size > 0)
            {
                var h = Item("Hibernation file", "Windows saves memory here for Hibernate and Fast Startup. Turning hibernation off deletes it, but your PC may start a little slower.", Safety.Review, false);
                h.InfoOnly = true; h.Size = he.Size; h.Measured = true;
                h.ActionText = "Turn off hibernation";
                h.Action = TurnOffHibernation;
                list.Add(h);
            }

            string old = Path.Combine(drive, "Windows.old");
            if (Directory.Exists(old))
            {
                var o = Item("Previous Windows version", "A copy of your old Windows, kept so you can go back after an upgrade. Remove it in Settings > Storage > Temporary files when you're happy.", Safety.Review, false);
                o.InfoOnly = true; o.Folders.Add(old);
                o.ActionText = "Open Storage settings";
                o.Action = () => Program.Shell("ms-settings:storagesense");
                list.Add(o);
            }

            foreach (var it in list)
                if (it.NeedsAdmin && !admin) { it.Blocked = true; it.Checked = false; }
            return list;
        }

        const string BrowserText = "Saved copies of web pages and images. You stay signed in - pages may load a bit slower the first time.";

        static void AddChromium(List<CleanItem> list, string name, string userData)
        {
            if (!Directory.Exists(userData)) return;
            var it = Item(name + " cache", BrowserText, Safety.Safe, true);
            foreach (var prof in SafeDirs(userData))
            {
                string n = Path.GetFileName(prof);
                if (n == "Default" || n.StartsWith("Profile ") || n == "Guest Profile")
                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
                        it.Folders.Add(Path.Combine(prof, sub));
            }
            foreach (var sub in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" })
                it.Folders.Add(Path.Combine(userData, sub));
            it.OpenPath = userData;
            Add(list, it);
        }

        static string[] SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
        }

        static CleanItem Item(string title, string desc, Safety level, bool on)
        {
            var it = new CleanItem();
            it.Title = title; it.Description = desc; it.Level = level; it.Checked = on;
            return it;
        }

        // Only show a category if at least one of its places exists on this PC.
        static void Add(List<CleanItem> list, CleanItem it)
        {
            it.Folders.RemoveAll(f => !Directory.Exists(f));
            it.Patterns.RemoveAll(p => !Directory.Exists(p.Split('|')[0]));
            if (it.Folders.Count == 0 && it.Patterns.Count == 0) return;
            if (it.OpenPath == null && it.Folders.Count == 1) it.OpenPath = it.Folders[0];
            list.Add(it);
        }

        static bool IsSafeRoot(string folder)
        {
            // Never empty a whole drive, Windows, or a profile even if an environment variable is odd.
            string f = folder.TrimEnd('\\');
            if (f.Length < 8) return false;
            foreach (var bad in new[] { Env(Environment.SpecialFolder.Windows), Env(Environment.SpecialFolder.UserProfile),
                Env(Environment.SpecialFolder.LocalApplicationData), Env(Environment.SpecialFolder.ApplicationData),
                Env(Environment.SpecialFolder.ProgramFiles), Env(Environment.SpecialFolder.ProgramFilesX86),
                Env(Environment.SpecialFolder.CommonApplicationData), Env(Environment.SpecialFolder.System) })
                if (string.Equals(f, bad.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public static void Measure(CleanItem it)
        {
            long size = 0, count = 0;
            if (it.IsRecycleBin)
            {
                var info = new Native.SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO));
                if (Native.SHQueryRecycleBinW(Path.GetPathRoot(Env(Environment.SpecialFolder.Windows)), ref info) == 0)
                { size = info.i64Size; count = info.i64NumItems; }
            }
            else if (it.InfoOnly && it.Measured)
            {
                return;
            }
            else
            {
                DateTime cutoff = Cutoff(it);
                foreach (var f in it.Folders) MeasureDir(f, cutoff, ref size, ref count);
                foreach (var p in it.Patterns) foreach (var e in Matches(p)) { size += e.Size; count++; }
            }
            it.Size = size;
            it.Count = count;
            it.Measured = true;
        }

        static DateTime Cutoff(CleanItem it)
        {
            return it.MinAgeHours > 0 ? DateTime.UtcNow.AddHours(-it.MinAgeHours) : DateTime.MaxValue;
        }

        static void MeasureDir(string dir, DateTime cutoff, ref long size, ref long count)
        {
            bool ok;
            foreach (var e in Native.ListDir(dir, out ok))
            {
                if (e.IsDir) { if (!e.IsLink) MeasureDir(Native.Join(dir, e.Name), cutoff, ref size, ref count); }
                else if (e.Modified <= cutoff && !e.CloudOnly) { size += e.Size; count++; }
            }
        }

        static List<Entry> Matches(string pattern)
        {
            var parts = pattern.Split('|');
            var result = new List<Entry>();
            try
            {
                foreach (var f in Directory.GetFiles(parts[0], parts[1]))
                {
                    Entry e;
                    if (Native.TryGetFile(f, out e)) { e.Name = f; result.Add(e); }
                }
            }
            catch { }
            return result;
        }

        public static CleanResult Clean(CleanItem it, IntPtr owner)
        {
            var r = new CleanResult();
            if (it.InfoOnly || it.Blocked) return r;
            if (it.IsRecycleBin)
            {
                // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
                Native.SHEmptyRecycleBinW(owner, Path.GetPathRoot(Env(Environment.SpecialFolder.Windows)), 0x7);
                r.Freed = it.Size; r.Deleted = it.Count;
                return r;
            }
            DateTime cutoff = Cutoff(it);
            foreach (var f in it.Folders)
                if (IsSafeRoot(f)) CleanDir(f, cutoff, r, false);
            foreach (var p in it.Patterns)
                foreach (var e in Matches(p)) DeleteFile(e.Name, e, r);
            return r;
        }

        static void CleanDir(string dir, DateTime cutoff, CleanResult r, bool removeSelf)
        {
            bool ok;
            var entries = Native.ListDir(dir, out ok);
            if (!ok) { r.Skipped++; return; }
            foreach (var e in entries)
            {
                string p = Native.Join(dir, e.Name);
                if (e.IsDir)
                {
                    if (!e.IsLink) CleanDir(p, cutoff, r, true); // never follow links out of the folder
                }
                else if (e.Modified <= cutoff)
                {
                    DeleteFile(p, e, r);
                }
            }
            if (removeSelf) Native.RemoveDirectoryW(Native.Long(dir)); // fails harmlessly if something was kept
        }

        static void DeleteFile(string path, Entry e, CleanResult r)
        {
            string lp = Native.Long(path);
            if ((e.Attr & Native.ATTR_READONLY) != 0) Native.SetFileAttributesW(lp, Native.ATTR_NORMAL);
            if (Native.DeleteFileW(lp)) { r.Freed += e.CloudOnly ? 0 : e.Size; r.Deleted++; }
            else r.Skipped++; // usually "in use" - that's fine
        }

        static void TurnOffHibernation()
        {
            var answer = System.Windows.Forms.MessageBox.Show(
                "Turn off hibernation?\n\nThis removes the hibernation file and frees its space. " +
                "Your PC will no longer be able to Hibernate, and Fast Startup is turned off too.\n\n" +
                "You can turn it back on any time by running  powercfg /hibernate on  as administrator.\n\n" +
                "Windows will ask for administrator permission.",
                "Turn off hibernation", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question);
            if (answer != System.Windows.Forms.DialogResult.Yes) return;
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe", "/hibernate off");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                var proc = Process.Start(psi);
                if (proc != null) proc.WaitForExit(15000);
            }
            catch { }
        }
    }
}
