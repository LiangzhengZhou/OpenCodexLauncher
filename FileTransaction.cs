using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;

namespace OpenCodexLauncherV2
{
    public sealed class RecoveryFile
    {
        public string Path { get; set; }
        public string BeforeFile { get; set; }
        public string ExpectedHash { get; set; }
    }
    // Journals live only in the user's private data directory, never beside the executable.
    public sealed class FileTransaction
    {
        public string DirectoryPath { get; private set; }
        readonly List<RecoveryFile> files = new List<RecoveryFile>();
        static readonly AsyncLocal<FileTransaction> active = new AsyncLocal<FileTransaction>();
        static RecoveryFile Tracked(string path)
        {
            return active.Value == null ? null : active.Value.files.FirstOrDefault(x => String.Equals(x.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        }
        public static void BeforeWrite(string path) { if (Tracked(path) != null) active.Value.Check(); }
        public static void AfterWrite(string path, string hash)
        {
            var item = Tracked(path);
            if (item != null) { item.ExpectedHash = hash; active.Value.Save("in-progress"); }
        }
        public static string Hash(string path)
        {
            if (!File.Exists(path)) return "missing";
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
        }
        public FileTransaction(IEnumerable<string> paths)
        {
            DirectoryPath = Path.Combine(LocalEnvironment.Current.DataDirectory, "recovery", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            foreach (var path in paths.Where(x => !String.IsNullOrEmpty(x)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var item = new RecoveryFile { Path = path, ExpectedHash = Hash(path) };
                if (File.Exists(path)) { item.BeforeFile = files.Count + ".original"; File.Copy(path, Path.Combine(DirectoryPath, item.BeforeFile)); }
                files.Add(item);
            }
            Save("prepared");
        }
        void Save(string state)
        {
            TextFile.AtomicWrite(Path.Combine(DirectoryPath, "journal.json"), JsonData.Serializer().Serialize(new { state = state, files = files }), new UTF8Encoding(false));
        }
        void Check()
        {
            if (files.Any(x => Hash(x.Path) != x.ExpectedHash)) throw new IOException(L.M("recovery.external") + DirectoryPath);
        }
        public async Task Step(Func<Task> action)
        {
            Check();
            var previous = active.Value;
            active.Value = this;
            try { await action(); Check(); }
            finally { active.Value = previous; Save("in-progress"); }
        }
        public void Commit() { Check(); Save("committed"); }
        public void Rollback()
        {
            Check();
            foreach (var item in files.AsEnumerable().Reverse())
            {
                if (item.BeforeFile == null) { if (File.Exists(item.Path)) File.Delete(item.Path); }
                else
                {
                    var original=Path.Combine(DirectoryPath,item.BeforeFile);
                    // A failed write may leave its destination untouched and locked.
                    // Do not rewrite that file and prevent other changed files restoring.
                    if(Hash(item.Path)!=Hash(original))AtomicBytes(item.Path,File.ReadAllBytes(original));
                }
                item.ExpectedHash = Hash(item.Path);
            }
            Save("rolled-back");
        }
        static void AtomicBytes(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllBytes(temp, bytes); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
