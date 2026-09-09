using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OpenCodexLauncherV2
{
    public sealed class LauncherRelease
    {
        public string Version { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
    }
    public sealed class LauncherUpdatePlan
    {
        public LauncherRelease Release { get; set; }
        public string Target { get; set; }
        public string DataDirectory { get; set; }
        public string UserDirectory { get; set; }
        public bool IsIsolated { get; set; }
        public string Language { get; set; }
        public string CurrentVersion { get; set; }
        public int ParentId { get; set; }
        public long ParentStart { get; set; }
        public Dictionary<string,string> Before { get; set; }
        public bool IsRollback { get; set; }
    }
    // Explicit, unauthenticated requests only. No configuration or credentials are sent.
    public sealed class LauncherUpdater
    {
        public const string Repository = "LiangzhengZhou/OpenCodexLauncher";
        public const string LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const long MaxZip = 32 * 1024 * 1024;
        public static readonly string[] Files = { "OpenCodexLauncher.exe", "OpenCodexLauncher.exe.config", "README.md", "README.zh-CN.md", "CHANGELOG.md", "RELEASE_NOTES.md", "LICENSE", "SHA256SUMS.txt" };
        public static string CurrentVersion { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }
        readonly Func<Uri,long,CancellationToken,Task<byte[]>> download;
        public LauncherUpdater() : this(Download) { }
        public LauncherUpdater(Func<Uri,long,CancellationToken,Task<byte[]>> transport) { download = transport; }
        static Exception Invalid() { return new InvalidDataException(L.M("update.invalid")); }
        public static Version ParseVersion(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"^(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$")) throw Invalid();
            return new Version(value);
        }
        public static void ValidateRelease(LauncherRelease release)
        {
            if (release == null) throw Invalid();
            ParseVersion(release.Version);
            if (release.Size < 1 || release.Size > MaxZip || release.Sha256 == null || !Regex.IsMatch(release.Sha256, "^[a-f0-9]{64}$")) throw Invalid();
            var expected = "https://github.com/" + Repository + "/releases/download/v" + release.Version + "/OpenCodexLauncher-" + release.Version + "-windows-x64.zip";
            if (release.Url != expected) throw Invalid();
        }
        public static LauncherRelease ParseRelease(string json, string current)
        {
            var raw = JsonData.Serializer().Deserialize<Dictionary<string,object>>(json);
            if (raw == null || !Object.Equals(JsonData.Value(raw,"draft"),false) || !Object.Equals(JsonData.Value(raw,"prerelease"),false)) throw Invalid();
            var tag = JsonData.Text(raw,"tag_name");
            if (!tag.StartsWith("v",StringComparison.Ordinal)) throw Invalid();
            var version = tag.Substring(1); var parsed = ParseVersion(version);
            if (parsed <= ParseVersion(current)) return null;
            var name = "OpenCodexLauncher-" + version + "-windows-x64.zip";
            var assets = JsonData.Array(JsonData.Value(raw,"assets")).Select(JsonData.Object).Where(x=>x != null && JsonData.Text(x,"name")==name).ToList();
            if (assets.Count != 1) throw Invalid();
            var asset = assets[0]; var digest = JsonData.Text(asset,"digest");
            if (!digest.StartsWith("sha256:",StringComparison.Ordinal)) throw Invalid();
            var release = new LauncherRelease { Version=version,Url=JsonData.Text(asset,"browser_download_url"),Sha256=digest.Substring(7),Size=Convert.ToInt64(JsonData.Value(asset,"size")) };
            ValidateRelease(release); return release;
        }
        public async Task<LauncherRelease> CheckAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var bytes = await download(new Uri(LatestUrl), 1024*1024, token);
            token.ThrowIfCancellationRequested();
            if (bytes.Length > 1024*1024) throw Invalid();
            return ParseRelease(Encoding.UTF8.GetString(bytes), CurrentVersion);
        }
        public static bool AllowedUri(Uri uri)
        {
            if (uri == null || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
            return uri.AbsoluteUri == LatestUrl ||
                (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal)) ||
                uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com";
        }
        public static async Task<byte[]> Download(Uri uri, long limit, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var handler = new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false, Credentials=null })
            using (var client = new HttpClient(handler))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(3)); client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenCodexLauncher/" + CurrentVersion);
                for (int hop=0; hop<6; hop++)
                {
                    if (!AllowedUri(uri)) throw Invalid();
                    using (var response = await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token))
                    {
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        {
                            if (response.Headers.Location == null) throw Invalid();
                            uri = new Uri(uri,response.Headers.Location); continue;
                        }
                        if (!response.IsSuccessStatusCode) throw new IOException(L.F("update.http",(int)response.StatusCode));
                        if (response.Content.Headers.ContentLength > limit) throw Invalid();
                        using (var input = await response.Content.ReadAsStreamAsync())
                        using (var output = new MemoryStream())
                        {
                            var buffer = new byte[16384]; int count;
                            while ((count=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token)) != 0)
                            { if (output.Length+count>limit) throw Invalid(); await output.WriteAsync(buffer,0,count,timeout.Token); }
                            return output.ToArray();
                        }
                    }
                }
                throw Invalid();
            }
        }
        public static string Hash(byte[] bytes)
        { using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        public static string FileHash(string path) { return File.Exists(path) ? Hash(File.ReadAllBytes(path)) : "missing"; }
        public static void NoLinks(string path)
        {
            for (var current=Path.GetFullPath(path); !String.IsNullOrEmpty(current); current=Path.GetDirectoryName(current))
                if ((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0) throw Invalid();
        }
        public static void VerifyPackage(string zip, string stage, LauncherRelease release)
        {
            ValidateRelease(release); NoLinks(zip); NoLinks(stage);
            if (new FileInfo(zip).Length != release.Size || FileHash(zip) != release.Sha256) throw Invalid();
            if (Directory.Exists(stage)) throw Invalid();
            Directory.CreateDirectory(stage);
            using (var archive = ZipFile.OpenRead(zip))
            {
                if (archive.Entries.Count != Files.Length) throw Invalid();
                var seen=new HashSet<string>(StringComparer.Ordinal); long total=0;
                foreach (var item in archive.Entries)
                {
                    if (!Files.Contains(item.FullName,StringComparer.Ordinal) || !seen.Add(item.FullName) || ((item.ExternalAttributes >> 16)&0xf000)==0xa000 || (item.ExternalAttributes&1024)!=0) throw Invalid();
                    total+=item.Length;
                    if (item.Length>32*1024*1024 || total>64*1024*1024) throw Invalid();
                    using (var input=item.Open()) using(var output=new FileStream(Path.Combine(stage,item.FullName),FileMode.CreateNew))
                    { var buffer=new byte[16384]; int n; long written=0; while((n=input.Read(buffer,0,buffer.Length))!=0) { written+=n; if(written>item.Length)throw Invalid();output.Write(buffer,0,n); } if(written!=item.Length)throw Invalid(); }
                }
            }
            VerifyStage(stage,release.Version);
        }
        public static void VerifyStage(string stage, string version)
        {
            NoLinks(stage);
            if (Directory.GetFileSystemEntries(stage).Length != Files.Length) throw Invalid();
            foreach(var name in Files) { var file=Path.Combine(stage,name);NoLinks(file);if(!File.Exists(file))throw Invalid(); }
            var sums=File.ReadAllLines(Path.Combine(stage,"SHA256SUMS.txt")); var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var line in sums)
            {
                var match=Regex.Match(line,@"^([a-f0-9]{64})  (.+)$");
                if(!match.Success)throw Invalid();var name=match.Groups[2].Value;
                if(name=="SHA256SUMS.txt" || !Files.Contains(name,StringComparer.Ordinal) || !seen.Add(name) || FileHash(Path.Combine(stage,name))!=match.Groups[1].Value)throw Invalid();
            }
            if(seen.Count!=Files.Length-1)throw Invalid();
            var exe=Path.Combine(stage,Files[0]);
            if(AssemblyName.GetAssemblyName(exe).Version!=new Version(version+".0") || FileVersionInfo.GetVersionInfo(exe).FileVersion!=version+".0")throw Invalid();
        }
        public static void CheckTarget(string target)
        {
            NoLinks(target); if(!Directory.Exists(target))throw Invalid();
            foreach(var name in Files)
            {
                var path=Path.Combine(target,name);NoLinks(path);
                if(Directory.Exists(path) || (File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReadOnly)!=0))throw new IOException(L.M("update.writable"));
            }
            var probe=Path.Combine(target,".launcher-update-"+Guid.NewGuid().ToString("N"));
            try { using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)){file.WriteByte(0);} }
            catch(Exception e){throw new IOException(L.M("update.writable"),e);}
        }
        public Task<string> StageAsync(LauncherRelease release, string target, CancellationToken token)
        { return StageCoreAsync(release,target,false,token); }
        public Task<string> StageRollbackAsync(string version, string target, CancellationToken token)
        { return StageCoreAsync(CriticalVersions.Find(version),target,true,token); }
        public static void ValidateTransition(LauncherRelease release, string current, bool rollback)
        {
            ValidateRelease(release);
            if(rollback) { CriticalVersions.Validate(release,current); CriticalVersions.CheckConfiguration(); }
            else if(ParseVersion(release.Version)<=ParseVersion(current))throw Invalid();
        }
        async Task<string> StageCoreAsync(LauncherRelease release, string target, bool rollback, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ValidateTransition(release,CurrentVersion,rollback);
            CheckTarget(target);
            var before=Files.ToDictionary(x=>x,x=>FileHash(Path.Combine(target,x)));
            var root=Path.Combine(LocalEnvironment.Current.DataDirectory,"updates",Guid.NewGuid().ToString("N"));
            NoLinks(root); Directory.CreateDirectory(root);
            var cached=rollback && CriticalVersions.HasCache(release);
            var bytes=cached ? File.ReadAllBytes(CriticalVersions.CachePath(release)) : await download(new Uri(release.Url),MaxZip,token); token.ThrowIfCancellationRequested();
            if(bytes.LongLength!=release.Size || Hash(bytes)!=release.Sha256)throw Invalid();
            var zip=Path.Combine(root,"package.zip");File.WriteAllBytes(zip,bytes);
            await Task.Run(()=>VerifyPackage(zip,Path.Combine(root,"payload"),release),token);
            token.ThrowIfCancellationRequested();
            ValidateTransition(release,CurrentVersion,rollback);
            if(rollback && !cached)
            {
                var cache=CriticalVersions.CachePath(release);NoLinks(cache);Directory.CreateDirectory(Path.GetDirectoryName(cache));
                if(!File.Exists(cache))
                {
                    var temporary=cache+"."+Guid.NewGuid().ToString("N");
                    try { File.Copy(zip,temporary);File.Move(temporary,cache); }
                    finally { if(File.Exists(temporary))File.Delete(temporary); }
                }
            }
            var process=Process.GetCurrentProcess();
            var plan=new LauncherUpdatePlan { Release=release,Target=Path.GetFullPath(target),DataDirectory=LocalEnvironment.Current.DataDirectory,Language=L.Language,CurrentVersion=CurrentVersion,ParentId=process.Id,ParentStart=process.StartTime.ToUniversalTime().Ticks,Before=before };
            plan.IsRollback=rollback;
            plan.UserDirectory=LocalEnvironment.Current.UserDirectory;plan.IsIsolated=LocalEnvironment.Current.IsIsolated;
            var planPath=Path.Combine(root,"plan.json");File.WriteAllText(planPath,JsonData.Serializer().Serialize(plan),new UTF8Encoding(false));
            File.Copy(Assembly.GetExecutingAssembly().Location,Path.Combine(root,"update-helper.exe"));
            File.Copy(Assembly.GetExecutingAssembly().Location+".config",Path.Combine(root,"update-helper.exe.config"));
            return planPath;
        }
        public static async Task<Process> StartHelperAsync(string planPath, CancellationToken token)
        {
            var root=Path.GetDirectoryName(planPath);
            var helper=Process.Start(new ProcessStartInfo(Path.Combine(root,"update-helper.exe"),"--apply-launcher-update "+Commands.Quote(planPath)) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root});
            try
            {
                for(int i=0;i<150;i++)
                { token.ThrowIfCancellationRequested(); if(helper.HasExited)throw new IOException(L.M("update.helper")); if(File.Exists(Path.Combine(root,"ready")))return helper;await Task.Delay(100,token); }
                throw new IOException(L.M("update.helper"));
            }
            catch { File.WriteAllText(Path.Combine(root,"cancel"),"");helper.Dispose();throw; }
        }
        public static async Task<string> ApplyAsync(LauncherUpdatePlan plan, string payload, Action<int> afterWrite, Action startInstalled = null)
        {
            ValidateRelease(plan.Release); VerifyStage(payload,plan.Release.Version); CheckTarget(plan.Target);
            ValidateTransition(plan.Release,plan.CurrentVersion,plan.IsRollback);
            if(plan.Before==null || plan.Before.Count!=Files.Length)throw Invalid();
            foreach(var name in Files)if(!plan.Before.ContainsKey(name)||FileHash(Path.Combine(plan.Target,name))!=plan.Before[name])throw new IOException(L.M("update.changed"));
            var transaction=new FileTransaction(Files.Select(x=>Path.Combine(plan.Target,x)));
            try
            {
                // Record recovery location before touching the installation, including if power is lost.
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(payload),"recovery.txt"),transaction.DirectoryPath);
                await transaction.Step(()=> {
                    for(int i=0;i<Files.Length;i++)
                    {
                        var path=Path.Combine(plan.Target,Files[i]);NoLinks(path);FileTransaction.BeforeWrite(path);
                        var temp=path+".update-"+Guid.NewGuid().ToString("N");
                        try { File.Copy(Path.Combine(payload,Files[i]),temp); if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path); }
                        finally { if(File.Exists(temp))File.Delete(temp); }
                        FileTransaction.AfterWrite(path,FileTransaction.Hash(path)); if(afterWrite!=null)afterWrite(i);
                    }
                    VerifyStageFiles(plan.Target,payload);return Task.FromResult(0);
                });
                if(startInstalled!=null)startInstalled();
                transaction.Commit();return transaction.DirectoryPath;
            }
            catch(Exception failure)
            {
                try { transaction.Rollback(); }
                catch(Exception rollback){throw new IOException(L.M("update.recovery")+transaction.DirectoryPath+"\n"+Redactor.Apply(rollback.Message),failure);}
                throw new IOException(L.M("update.rolledBack")+transaction.DirectoryPath,failure);
            }
        }
        static void VerifyStageFiles(string target,string payload)
        { foreach(var name in Files)if(FileHash(Path.Combine(target,name))!=FileHash(Path.Combine(payload,name)))throw Invalid(); }
        public static int RunHelper(string planPath)
        {
            string root=null;
            try
            {
                root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if(Path.GetFullPath(planPath)!=Path.Combine(root,"plan.json"))throw Invalid();NoLinks(root);
                if(new FileInfo(planPath).Length>65536)throw Invalid();
                var plan=JsonData.Serializer().Deserialize<LauncherUpdatePlan>(File.ReadAllText(planPath));
                L.SetLanguage(plan.Language);ValidateRelease(plan.Release);
                if(plan.CurrentVersion!=CurrentVersion || !String.Equals(Path.GetDirectoryName(root),Path.Combine(Path.GetFullPath(plan.DataDirectory),"updates"),StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(plan.Target)==root)throw Invalid();
                if(String.IsNullOrWhiteSpace(plan.UserDirectory) || !Path.IsPathRooted(plan.UserDirectory))throw Invalid();
                LocalEnvironment.Current=new LocalEnvironment(plan.DataDirectory,plan.UserDirectory,plan.IsIsolated);
                CheckTarget(plan.Target);
                // A per-installation mutex serializes helpers across launcher windows.
                using(var mutex=new Mutex(false,"Local\\OpenCodexLauncher.Update."+Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(plan.Target).ToUpperInvariant()))))
                {
                    bool held=false;
                    try
                    {
                        try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException(L.M("update.busy"));
                        using(var parent=Process.GetProcessById(plan.ParentId))
                        {
                            if(parent.StartTime.ToUniversalTime().Ticks!=plan.ParentStart || !String.Equals(parent.MainModule.FileName,Path.Combine(plan.Target,Files[0]),StringComparison.OrdinalIgnoreCase))throw Invalid();
                            // Re-extract from the authenticated package. Never trust an editable payload directory.
                            var payload=Path.Combine(root,"verified-"+Guid.NewGuid().ToString("N"));VerifyPackage(Path.Combine(root,"package.zip"),payload,plan.Release);
                            File.WriteAllText(Path.Combine(root,"ready"),"");
                            var deadline=DateTime.UtcNow.AddSeconds(90);
                            while(!parent.WaitForExit(200)) {if(File.Exists(Path.Combine(root,"cancel"))||DateTime.UtcNow>deadline)throw new IOException(L.M("update.cancelled"));}
                            if(File.Exists(Path.Combine(root,"cancel")))throw new IOException(L.M("update.cancelled"));
                            ValidateTransition(plan.Release,plan.CurrentVersion,plan.IsRollback);
                            ApplyAsync(plan,payload,null,()=> {
                                using(var started=Process.Start(new ProcessStartInfo(Path.Combine(plan.Target,Files[0])){UseShellExecute=false,WorkingDirectory=plan.Target}))
                                { if(started==null)throw new IOException(L.M("update.helper")); }
                            }).GetAwaiter().GetResult();
                            File.WriteAllText(Path.Combine(root,"result.txt"),"Updated to "+plan.Release.Version);
                        }
                    }
                    finally {if(held)mutex.ReleaseMutex();}
                }
                return 0;
            }
            catch(Exception e)
            {
                var error=Redactor.Apply(e.Message);
                if(root!=null) {try{File.WriteAllText(Path.Combine(root,"error.txt"),error);}catch{}}
                MessageBox.Show(L.M("update.failed")+"\n"+error,"OpenCodex Launcher",MessageBoxButton.OK,MessageBoxImage.Warning);return 1;
            }
        }
    }
}
