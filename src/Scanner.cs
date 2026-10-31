using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CDriveCleaner
{
    class DirNode
    {
        public string Name;
        public DirNode Parent;
        public List<DirNode> Children;
        public long Size, Files, OwnSize, OwnFiles;
        public bool Denied;

        public string Path
        {
            get { return Parent == null ? Name : Native.Join(Parent.Path, Name); }
        }

        public DirNode Child(string name)
        {
            if (Children == null) return null;
            foreach (var c in Children)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }
    }

    class FileItem
    {
        public string Path;
        public long Size;
        public DateTime Modified;
    }

    // Walks the whole drive once and keeps a tree of folder sizes plus the largest files.
    class DriveScanner
    {
        const int MaxTop = 300;

        public readonly string RootPath;
        public DirNode Root;
        public long FilesSeen, BytesSeen, FoldersSeen;
        public int DeniedCount;
        public volatile string Current = "";
        public volatile bool CancelRequested;
        public DateTime Started, Finished;
        public List<FileItem> LargestFiles = new List<FileItem>();
        public string Error;

        readonly object topLock = new object();
        readonly List<FileItem> top = new List<FileItem>();
        long topMin = 1L << 20;

        public DriveScanner(string root) { RootPath = root; }

        public void Run()
        {
            Started = DateTime.Now;
            Root = new DirNode();
            Root.Name = RootPath;
            ScanDir(Root, RootPath, 0);
            lock (topLock)
            {
                top.Sort(BySizeDesc);
                if (top.Count > MaxTop) top.RemoveRange(MaxTop, top.Count - MaxTop);
                LargestFiles = new List<FileItem>(top);
            }
            Finished = DateTime.Now;
        }

        static int BySizeDesc(FileItem a, FileItem b) { return b.Size.CompareTo(a.Size); }

        void ScanDir(DirNode node, string path, int depth)
        {
            if (CancelRequested) return;
            Current = path;
            bool ok;
            var entries = Native.ListDir(path, out ok);
            if (!ok)
            {
                node.Denied = true;
                Interlocked.Increment(ref DeniedCount);
                return;
            }

            List<DirNode> subs = null;
            long own = 0, ownFiles = 0;
            foreach (var e in entries)
            {
                if (e.IsDir)
                {
                    if (e.IsLink) continue; // junctions point at folders we already count
                    var c = new DirNode();
                    c.Name = e.Name;
                    c.Parent = node;
                    if (subs == null) subs = new List<DirNode>();
                    subs.Add(c);
                }
                else
                {
                    long sz = e.CloudOnly ? 0 : Native.OnDiskSize(Native.Join(path, e.Name), e.Size); // OneDrive online-only files use no disk space
                    own += sz;
                    ownFiles++;
                    if (sz >= Interlocked.Read(ref topMin)) AddTop(Native.Join(path, e.Name), sz, e.Modified);
                }
            }
            node.OwnSize = own;
            node.OwnFiles = ownFiles;
            Interlocked.Add(ref FilesSeen, ownFiles);
            Interlocked.Add(ref BytesSeen, own);
            Interlocked.Increment(ref FoldersSeen);

            long size = own, files = ownFiles;
            if (subs != null)
            {
                if (depth < 3 && subs.Count > 1)
                {
                    var opts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                    Parallel.ForEach(subs, opts, c => ScanDir(c, Native.Join(path, c.Name), depth + 1));
                }
                else
                {
                    foreach (var c in subs) ScanDir(c, Native.Join(path, c.Name), depth + 1);
                }
                subs.Sort((a, b) => b.Size.CompareTo(a.Size));
                node.Children = subs;
                foreach (var c in subs) { size += c.Size; files += c.Files; }
            }
            node.Size = size;
            node.Files = files;
        }

        void AddTop(string path, long size, DateTime modified)
        {
            lock (topLock)
            {
                top.Add(new FileItem { Path = path, Size = size, Modified = modified });
                if (top.Count >= MaxTop * 2)
                {
                    top.Sort(BySizeDesc);
                    top.RemoveRange(MaxTop, top.Count - MaxTop);
                    Interlocked.Exchange(ref topMin, top[top.Count - 1].Size);
                }
            }
        }

        public DirNode Find(string path)
        {
            if (Root == null || path == null) return null;
            string root = RootPath.TrimEnd('\\');
            string p = path.TrimEnd('\\');
            if (!p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            string rest = p.Substring(root.Length).Trim('\\');
            DirNode n = Root;
            if (rest.Length == 0) return n;
            foreach (var part in rest.Split('\\'))
            {
                n = n.Child(part);
                if (n == null) return null;
            }
            return n;
        }

        // Keeps the tree in step after the user deletes something from inside the app.
        public void Forget(DirNode node)
        {
            if (node == null || node.Parent == null) return;
            node.Parent.Children.Remove(node);
            for (var a = node.Parent; a != null; a = a.Parent) { a.Size -= node.Size; a.Files -= node.Files; }
            string prefix = node.Path + "\\";
            LargestFiles.RemoveAll(f => f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public void ForgetFile(string path, long size)
        {
            var dir = Find(Path.GetDirectoryName(path));
            if (dir != null)
            {
                dir.OwnSize -= size;
                dir.OwnFiles--;
                for (var a = dir; a != null; a = a.Parent) { a.Size -= size; a.Files--; }
            }
            LargestFiles.RemoveAll(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
