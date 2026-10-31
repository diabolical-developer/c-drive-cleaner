using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace CDriveCleaner
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                Theme.Init(g.DpiX);
            Application.Run(new MainForm());
        }

        static bool? admin;
        public static bool IsAdmin()
        {
            if (admin == null)
            {
                try { admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
                catch { admin = false; }
            }
            return admin.Value;
        }

        // Returns false if the user said "No" to the Windows permission prompt.
        public static bool RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        public static void OpenInExplorer(string path, bool select)
        {
            try
            {
                if (select) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }

        public static void Shell(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }
    }

    static class Fmt
    {
        public static string Size(long bytes)
        {
            if (bytes <= 0) return "0 KB";
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            if (i == 0) return "1 KB";
            return v.ToString(v >= 100 ? "0" : v >= 10 ? "0.0" : "0.00") + " " + units[i];
        }

        public static string Count(long n) { return n.ToString("N0"); }

        public static string Duration(TimeSpan t)
        {
            if (t.TotalMinutes >= 1) return (int)t.TotalMinutes + " min " + t.Seconds + " s";
            return Math.Max(1, (int)t.TotalSeconds) + " s";
        }
    }

    struct Entry
    {
        public string Name;
        public bool IsDir, IsLink, CloudOnly;
        public long Size;
        public uint Attr;
        public DateTime Modified; // UTC
    }

    static class Native
    {
        public const uint ATTR_READONLY = 0x1, ATTR_DIRECTORY = 0x10, ATTR_REPARSE = 0x400, ATTR_OFFLINE = 0x1000,
            ATTR_RECALL_ON_OPEN = 0x40000, ATTR_RECALL_ON_DATA = 0x400000, ATTR_NORMAL = 0x80;
        const uint TAG_MOUNT_POINT = 0xA0000003, TAG_SYMLINK = 0xA000000C;
        static readonly IntPtr INVALID = new IntPtr(-1);
        const long MaxFileTime = 2650467743999999999L;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FIND_DATA
        {
            public uint Attributes;
            public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint SizeHigh, SizeLow, Reserved0, Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AltName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileExW(string name, int infoLevel, out FIND_DATA data, int searchOp, IntPtr filter, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool FindNextFileW(IntPtr h, out FIND_DATA data);
        [DllImport("kernel32.dll")]
        static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DeleteFileW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool RemoveDirectoryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetFileAttributesW(string path, uint attr);

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBinW(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBinW(IntPtr hwnd, string root, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetCompressedFileSizeW(string path, out uint high);

        // Real space used on disk. Big disk images (emulators, VMs) are often "sparse" and use far less than their size says.
        public static long OnDiskSize(string path, long size)
        {
            if (size < (256L << 20)) return size;
            uint high;
            uint low = GetCompressedFileSizeW(Long(path), out high);
            if (low == 0xFFFFFFFF && Marshal.GetLastWin32Error() != 0) return size;
            return Math.Min(size, ((long)high << 32) | low);
        }

        public static string Long(string path)
        {
            if (path.StartsWith(@"\\?\")) return path;
            if (path.StartsWith(@"\\")) return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

        public static string Join(string dir, string name)
        {
            return dir.EndsWith("\\") ? dir + name : dir + "\\" + name;
        }

        // Lists one folder quickly. ok=false when the folder can't be read (no permission, missing).
        public static List<Entry> ListDir(string dir, out bool ok)
        {
            var list = new List<Entry>();
            FIND_DATA fd;
            IntPtr h = FindFirstFileExW(Long(dir.TrimEnd('\\') + "\\*"), 1, out fd, 0, IntPtr.Zero, 2);
            if (h == INVALID)
            {
                ok = Marshal.GetLastWin32Error() == 2;
                return list;
            }
            ok = true;
            try
            {
                do
                {
                    string n = fd.FileName;
                    if (n == "." || n == "..") continue;
                    var e = new Entry();
                    e.Name = n;
                    e.Attr = fd.Attributes;
                    e.IsDir = (fd.Attributes & ATTR_DIRECTORY) != 0;
                    e.IsLink = (fd.Attributes & ATTR_REPARSE) != 0 && (fd.Reserved0 == TAG_MOUNT_POINT || fd.Reserved0 == TAG_SYMLINK);
                    e.Size = e.IsDir ? 0 : ((long)fd.SizeHigh << 32) | fd.SizeLow;
                    e.CloudOnly = (fd.Attributes & (ATTR_OFFLINE | ATTR_RECALL_ON_OPEN | ATTR_RECALL_ON_DATA)) != 0;
                    long ft = ((long)fd.WriteHigh << 32) | fd.WriteLow;
                    e.Modified = ft >= 0 && ft <= MaxFileTime ? DateTime.FromFileTimeUtc(ft) : DateTime.MinValue;
                    list.Add(e);
                } while (FindNextFileW(h, out fd));
            }
            finally { FindClose(h); }
            return list;
        }

        public static bool TryGetFile(string path, out Entry entry)
        {
            entry = new Entry();
            bool ok;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            if (dir == null) return false;
            foreach (var e in ListDir(dir, out ok))
                if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) { entry = e; return true; }
            return false;
        }
    }

    enum Safety { Safe, Review, Personal, App, System }

    class Advice
    {
        public Safety Level;
        public string Text;
        public bool Protected; // deletion blocked from inside the app
        public Advice(Safety l, string t) { Level = l; Text = t; }
    }

    // Explains in plain words what a folder or file on C: is, and whether it's OK to remove.
    static class Advisor
    {
        static readonly string win = Low(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        static readonly string user = Low(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        static readonly string local = Low(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        static readonly string roaming = Low(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        static readonly string temp = Low(Path.GetTempPath());
        static readonly string pf = Low(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        static readonly string pf86 = Low(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        static readonly string pd = Low(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        static readonly string drive = Low(Path.GetPathRoot(win));

        static readonly HashSet<string> cacheNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "cache", "cache2", "code cache", "gpucache", "cachedata", "cache_data", "shadercache", "grshadercache", "dawncache", "inetcache", "npm-cache", "cachedextensionvsixs" };

        static string Low(string p) { return string.IsNullOrEmpty(p) ? "\u0001" : p.TrimEnd('\\').ToLowerInvariant(); }
        static bool Under(string p, string root) { return p == root || p.StartsWith(root + "\\"); }

        public static Advice For(string path, bool isDir)
        {
            string p = Low(path);
            string name = Path.GetFileName(p);
            string ext = isDir ? "" : Path.GetExtension(p);

            if (p == drive || p == drive.TrimEnd(':') + ":")
                return Prot(Safety.System, "Your C: drive");

            // Places that are safe to clean
            if (Under(p, temp) || Under(p, win + "\\temp"))
                return new Advice(Safety.Safe, "Temporary files - safe to clean from the Clean up page");
            if (Under(p, win + "\\softwaredistribution\\download"))
                return new Advice(Safety.Safe, "Old Windows Update downloads - safe to clean from the Clean up page");
            if (Under(p, drive + "\\$recycle.bin"))
                return Prot(Safety.Safe, "Recycle Bin - empty it from the Clean up page");

            // Windows itself
            if (Under(p, drive + "\\windows.old"))
                return Prot(Safety.Review, "Your previous Windows version - remove it from Windows Storage settings");
            if (p == drive + "\\pagefile.sys" || p == drive + "\\swapfile.sys")
                return Prot(Safety.System, "Windows virtual memory - managed by Windows, don't delete");
            if (p == drive + "\\hiberfil.sys")
                return Prot(Safety.System, "Hibernation file - removed if you turn off hibernation (Clean up page)");
            if (Under(p, win + "\\winsxs"))
                return Prot(Safety.System, "Windows component store - looks bigger than it is (shared files). Don't delete");
            if (Under(p, win))
                return Prot(Safety.System, "Windows system files - don't delete");
            if (Under(p, drive + "\\system volume information"))
                return Prot(Safety.System, "System Restore points - manage in System Protection settings");
            if (p.StartsWith(drive + "\\$") || p == drive + "\\recovery" || p == drive + "\\boot" || p == drive + "\\bootmgr")
                return Prot(Safety.System, "Hidden Windows system area - don't delete");

            // Caches anywhere
            if (isDir && cacheNames.Contains(name))
                return new Advice(Safety.Safe, "Cache - safe to delete (close the app first). It rebuilds itself");
            if (p.Contains("\\pip\\cache") || p.Contains("\\.nuget\\packages") || p.Contains("\\.gradle\\caches") || p.Contains("\\yarn\\cache") || p.Contains("\\nuget\\v3-cache"))
                return new Advice(Safety.Safe, "Developer package cache - safe to delete, downloads again when needed");
            if (ext == ".dmp" || ext == ".tmp")
                return new Advice(Safety.Safe, ext == ".dmp" ? "Crash dump - safe to delete" : "Temporary file - safe to delete");
            if (isDir && name == "node_modules")
                return new Advice(Safety.Review, "Project packages - can be restored with 'npm install'");

            // Programs
            if (p == pf || p == pf86)
                return Prot(Safety.App, "Installed programs - remove them from the Installed apps page");
            if (Under(p, pf) || Under(p, pf86))
                return Prot(Safety.App, "Part of an installed program - uninstall it from the Installed apps page");
            if (p == pd)
                return Prot(Safety.App, "Shared data used by your programs - don't delete manually");
            if (Under(p, pd))
                return new Advice(Safety.App, "Data used by a program - delete only if that program is uninstalled");

            // The user's own stuff
            if (p == drive + "\\users" || p == user)
                return Prot(Safety.Personal, "Your user folder - open it to see what's inside");
            if (p == user + "\\appdata" || p == local || p == roaming || p == user + "\\appdata\\locallow")
                return Prot(Safety.Review, "Settings and data of your apps - open it to see which apps use the most");
            if (Under(p, local + "\\packages"))
                return new Advice(Safety.App, "Microsoft Store app data - uninstall the app to remove it");
            if (Under(p, user + "\\appdata"))
                return new Advice(Safety.Review, "App data & settings - delete only if you've uninstalled the app");
            if (Under(p, user + "\\downloads"))
            {
                if (p == user + "\\downloads") return Prot(Safety.Personal, "Your downloads - open it and delete what you no longer need");
                if (ext == ".exe" || ext == ".msi" || ext == ".zip" || ext == ".iso" || ext == ".rar" || ext == ".7z")
                    return new Advice(Safety.Personal, "Downloaded installer/archive - usually fine to delete once used");
                return new Advice(Safety.Personal, "Something you downloaded - delete it if you no longer need it");
            }
            if (Under(p, user) && name.StartsWith("onedrive") && p.Substring(user.Length).IndexOf('\\', 1) < 0)
                return Prot(Safety.Personal, "OneDrive - right-click files and choose 'Free up space' to keep them online only");
            if (Under(p, user + "\\onedrive"))
                return new Advice(Safety.Personal, "Synced with OneDrive - 'Free up space' keeps it online without deleting");
            foreach (var f in new[] { "documents", "desktop", "pictures", "videos", "music" })
            {
                if (p == user + "\\" + f) return Prot(Safety.Personal, "Your " + f + " - open it and review carefully");
                if (Under(p, user + "\\" + f)) return new Advice(Safety.Personal, "Your personal file - review carefully, or move it to another drive");
            }
            if (Under(p, drive + "\\users"))
                return new Advice(Safety.Personal, "User files - review before deleting");

            if (ext == ".iso" || ext == ".zip" || ext == ".msi")
                return new Advice(Safety.Review, "Installer or archive - often fine to delete once used");
            return new Advice(Safety.Review, "Not sure what this is - review before deleting");
        }

        static Advice Prot(Safety l, string t) { var a = new Advice(l, t); a.Protected = true; return a; }
    }
}
