using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    public sealed class OpenCodexRelease
    {
        public string Version { get; private set; }
        public string Tarball { get; private set; }
        public string Integrity { get; private set; }
        public static OpenCodexRelease Parse(string json)
        {
            var data = JsonData.Parse(json); var dist = JsonData.Object(JsonData.Value(data, "dist"));
            var version = JsonData.Text(data, "version"); StableVersion(version);
            var url = JsonData.Text(dist, "tarball"); var integrity = JsonData.Text(dist, "integrity");
            if (JsonData.Text(data,"name") != "@bitkyc08/opencodex" || url != "https://registry.npmjs.org/@bitkyc08/opencodex/-/opencodex-"+version+".tgz"
                || !Regex.IsMatch(integrity,@"\Asha512-[A-Za-z0-9+/]{86}==\z")) throw new InvalidDataException(L.M("install.metadata"));
            InstallerPlatform.TrustedUri(url);
            return new OpenCodexRelease { Version=version, Tarball=url, Integrity=integrity };
        }
        public static Version StableVersion(string value)
        {
            Version parsed;
            if (value == null || !Regex.IsMatch(value,@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") || !System.Version.TryParse(value,out parsed))
                throw new InvalidDataException(L.M("install.metadata"));
            return parsed;
        }
        public bool IsNewerThan(string current)
        { try { return StableVersion(Version) > StableVersion(current); } catch (InvalidDataException) { return true; } }
    }

    // Only this adapter touches the network or starts processes. Tests replace it.
    public interface IInstallerPlatform
    {
        Task<string> ReadAsync(string url, CancellationToken token);
        Task DownloadAsync(string url, string file, CancellationToken token);
        Task<string> RunAsync(string executable, string[] args, string directory, string home, CancellationToken token);
    }

    public sealed class InstallerPlatform : IInstallerPlatform
    {
        public static Uri TrustedUri(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value,UriKind.Absolute,out uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                (uri.Host != "nodejs.org" && uri.Host != "registry.npmjs.org")) throw new InvalidDataException(L.M("install.metadata"));
            return uri;
        }
        async Task Transfer(string url, Stream output, long maximum, CancellationToken token)
        {
            using (var handler = new HttpClientHandler { AllowAutoRedirect=false, UseDefaultCredentials=false, SslProtocols=System.Security.Authentication.SslProtocols.Tls12 })
            using (var client = new HttpClient(handler) { Timeout=TimeSpan.FromMinutes(10) })
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(10));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenCodexLauncher/2.5.2");
                using (var response = await client.GetAsync(TrustedUri(url),HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        long count=0; int read; var buffer=new byte[65536];
                        while ((read=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false))>0)
                        {
                            count+=read; if(count>maximum) throw new InvalidDataException(L.M("install.size"));
                            await output.WriteAsync(buffer,0,read,timeout.Token).ConfigureAwait(false);
                        }
                    }
                }
            }
        }
        public async Task<string> ReadAsync(string url,CancellationToken token)
        {
            using(var memory=new MemoryStream()) { await Transfer(url,memory,8*1024*1024,token).ConfigureAwait(false); return Encoding.UTF8.GetString(memory.ToArray()); }
        }
        public async Task DownloadAsync(string url,string file,CancellationToken token)
        {
            for(int attempt=0;;attempt++)
            {
                try { using(var output=new FileStream(file,FileMode.Create,FileAccess.Write,FileShare.None)) await Transfer(url,output,512L*1024*1024,token).ConfigureAwait(false); return; }
                catch(HttpRequestException) { if(attempt>=2)throw; }
                await Task.Delay(1000*(attempt+1),token).ConfigureAwait(false);
            }
        }
        public static ProcessStartInfo ProcessInfo(string executable,string[] args,string directory,string home)
        {
            // Do not inherit npm tokens, provider credentials, Node preload options or user npmrc.
            var info=new ProcessStartInfo(executable,String.Join(" ",args.Select(Commands.Quote))) {
                WorkingDirectory=directory, UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
            info.EnvironmentVariables.Clear();
            var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            info.EnvironmentVariables["SystemRoot"]=windows; info.EnvironmentVariables["WINDIR"]=windows;
            info.EnvironmentVariables["COMSPEC"]=Path.Combine(windows,"System32","cmd.exe");
            info.EnvironmentVariables["PATH"]=Path.GetDirectoryName(executable)+";"+Path.Combine(windows,"System32")+";"+windows;
            info.EnvironmentVariables["PATHEXT"]=".COM;.EXE;.BAT;.CMD";
            info.EnvironmentVariables["USERPROFILE"]=home; info.EnvironmentVariables["HOME"]=home;
            info.EnvironmentVariables["APPDATA"]=Path.Combine(home,"AppData","Roaming");
            info.EnvironmentVariables["LOCALAPPDATA"]=Path.Combine(home,"AppData","Local");
            info.EnvironmentVariables["TEMP"]=Path.Combine(home,"temp"); info.EnvironmentVariables["TMP"]=Path.Combine(home,"temp");
            info.EnvironmentVariables["OPENCODEX_HOME"]=Path.Combine(home,"opencodex"); info.EnvironmentVariables["CODEX_HOME"]=Path.Combine(home,"codex");
            info.EnvironmentVariables["npm_config_userconfig"]=Path.Combine(home,"user.npmrc");
            info.EnvironmentVariables["npm_config_globalconfig"]=Path.Combine(home,"global.npmrc");
            info.EnvironmentVariables["npm_config_cache"]=Path.Combine(home,"npm-cache");
            info.EnvironmentVariables["npm_config_registry"]="https://registry.npmjs.org/";
            info.EnvironmentVariables["npm_config_audit"]="false"; info.EnvironmentVariables["npm_config_fund"]="false";
            return info;
        }
        public async Task<string> RunAsync(string executable,string[] args,string directory,string home,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            foreach(var sub in new[]{"temp","AppData/Local","AppData/Roaming","opencodex","codex"})Directory.CreateDirectory(Path.Combine(home,sub));
            File.WriteAllText(Path.Combine(home,"user.npmrc"),""); File.WriteAllText(Path.Combine(home,"global.npmrc"),"");
            using(var job=new InstallerJob())
            using(var process=new Process {StartInfo=ProcessInfo(executable,args,directory,home)})
            {
                var output=new StringBuilder(); object sync=new object();
                DataReceivedEventHandler receive=(sender,e)=>{if(e.Data!=null)lock(sync){output.AppendLine(e.Data);if(output.Length>64000)output.Remove(0,output.Length-64000);}};
                process.OutputDataReceived+=receive;process.ErrorDataReceived+=receive;
                process.Start();
                try { job.Assign(process); } catch { try{process.Kill();}catch{} throw; }
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                try
                {
                    var end=DateTime.UtcNow.AddMinutes(12);
                    while(!process.HasExited){token.ThrowIfCancellationRequested();if(DateTime.UtcNow>end)throw new TimeoutException(L.M("install.timeout"));await Task.Delay(150,token).ConfigureAwait(false);}
                    process.WaitForExit();token.ThrowIfCancellationRequested();
                    if(process.ExitCode!=0)throw new IOException(L.M("install.process")+process.ExitCode+"\n"+Redactor.Apply(output.ToString()));
                    return output.ToString();
                }
                finally { job.Stop(); if(!process.HasExited)process.WaitForExit(5000); }
            }
        }
    }

    // Closing the job kills only processes started by this installer, including npm/Bun children.
    sealed class InstallerJob : IDisposable
    {
        IntPtr handle;
        [StructLayout(LayoutKind.Sequential)]struct Basic {public long PerProcess,PerJob;public uint Flags;public UIntPtr Min,Max;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
        [StructLayout(LayoutKind.Sequential)]struct Io {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential)]struct Extended {public Basic Basic;public Io Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int type,ref Extended info,uint length);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll")]static extern bool TerminateJobObject(IntPtr job,uint code);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        public InstallerJob()
        {
            handle=CreateJobObject(IntPtr.Zero,null);var info=new Extended();info.Basic.Flags=0x2000;
            if(handle==IntPtr.Zero || !SetInformationJobObject(handle,9,ref info,(uint)Marshal.SizeOf(typeof(Extended)))){Dispose();throw new IOException(L.M("install.job"));}
        }
        public void Assign(Process process){if(!AssignProcessToJobObject(handle,process.Handle))throw new IOException(L.M("install.job"));}
        public void Stop(){if(handle!=IntPtr.Zero)TerminateJobObject(handle,1);}
        public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    }

    public sealed class OpenCodexInstaller
    {
        readonly IInstallerPlatform platform;
        readonly string root;
        public OpenCodexInstaller(string directory,IInstallerPlatform platform)
        {
            // npm dependency trees exceed MAX_PATH even with ordinary user profile names.
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
            root=Path.GetFullPath(directory);this.platform=platform;
        }
        public Task<OpenCodexRelease> LatestAsync(CancellationToken token){return ReadLatest(token);}
        async Task<OpenCodexRelease> ReadLatest(CancellationToken token){return OpenCodexRelease.Parse(await platform.ReadAsync("https://registry.npmjs.org/@bitkyc08%2fopencodex/latest",token).ConfigureAwait(false));}
        public static string ReadInstalledVersion(string entry)
        {
            try
            {
                if(String.IsNullOrWhiteSpace(entry))return null;
                var dir=Path.GetDirectoryName(entry);
                var candidates=new[]{Path.Combine(dir,"..","package.json"),Path.Combine(dir,"node_modules","@bitkyc08","opencodex","package.json")};
                foreach(var file in candidates){if(!File.Exists(file))continue;var data=JsonData.Read(file);if(JsonData.Text(data,"name")=="@bitkyc08/opencodex")return JsonData.Text(data,"version");}
            }catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}
            return null;
        }
        public static string ManagedNode(string entry)
        {
            if(String.IsNullOrWhiteSpace(entry))return null;
            var root=Path.GetFullPath(Path.Combine(LocalEnvironment.Current.DataDirectory,"runtimes"))+Path.DirectorySeparatorChar;
            var full=Path.GetFullPath(entry);
            var suffix=Path.Combine("app","node_modules","@bitkyc08","opencodex","bin","ocx.mjs");
            if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !full.EndsWith(Path.DirectorySeparatorChar+suffix,StringComparison.OrdinalIgnoreCase))return null;
            var node=Path.Combine(full.Substring(0,full.Length-suffix.Length),"node","node.exe");
            return File.Exists(node)?node:null;
        }
        public static string SelectNode(string index)
        {
            var rows=JsonData.Array(JsonData.Serializer().DeserializeObject(index));
            return rows.Select(JsonData.Object).Where(x=>JsonData.Value(x,"lts") is string && JsonData.Array(JsonData.Value(x,"files")).Contains("win-x64-zip"))
                .Select(x=>JsonData.Text(x,"version")).Where(x=>Regex.IsMatch(x,@"\Av[0-9]+\.[0-9]+\.[0-9]+\z"))
                .Where(x=>OpenCodexRelease.StableVersion(x.Substring(1)).Major>=22)
                .OrderByDescending(x=>OpenCodexRelease.StableVersion(x.Substring(1))).FirstOrDefault() ?? throwNode();
        }
        static string throwNode(){throw new InvalidDataException(L.M("install.metadata"));}
        public static void Verify(string file,string expected,bool sha512)
        {
            using(var algorithm=sha512?(HashAlgorithm)SHA512.Create():SHA256.Create())using(var stream=File.OpenRead(file))
            {
                var hash=algorithm.ComputeHash(stream);var actual=sha512?"sha512-"+Convert.ToBase64String(hash):BitConverter.ToString(hash).Replace("-","").ToLowerInvariant();
                if(!String.Equals(actual,expected,StringComparison.Ordinal))throw new InvalidDataException(L.M("install.hash"));
            }
        }
        public static void ExtractNode(string file,string destination,CancellationToken token)
        {
            var root=Path.GetFullPath(destination)+Path.DirectorySeparatorChar;Directory.CreateDirectory(destination);long total=0;
            using(var zip=ZipFile.OpenRead(file))foreach(var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                var name=entry.FullName.Replace('/',Path.DirectorySeparatorChar);var split=name.IndexOf(Path.DirectorySeparatorChar);
                if(split<0)throw new InvalidDataException(L.M("install.archive"));
                name=name.Substring(split+1);if(name.Length==0)continue;
                var target=Path.GetFullPath(Path.Combine(root,name));total+=entry.Length;
                if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase)||name.Contains(":")||((entry.ExternalAttributes>>16)&0xF000)==0xA000||total>2L*1024*1024*1024)throw new InvalidDataException(L.M("install.archive"));
                if(name.EndsWith(Path.DirectorySeparatorChar.ToString()))Directory.CreateDirectory(ExtendedPath(target));
                else {Directory.CreateDirectory(ExtendedPath(Path.GetDirectoryName(target)));using(var input=entry.Open())using(var output=new FileStream(ExtendedPath(target),FileMode.CreateNew))input.CopyTo(output);}
            }
        }
        static string ExtendedPath(string path){return path.StartsWith(@"\\")?@"\\?\UNC\"+path.Substring(2):@"\\?\"+path;}
        public async Task<string> InstallAsync(OpenCodexRelease release,Action<string> progress,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var stage=Path.Combine(root,"ocx-"+release.Version+"-"+Guid.NewGuid().ToString("N"));
            string phase="install.prepare";
            Action<string> report=key=>{phase=key;progress(key);};
            try
            {
                Directory.CreateDirectory(root);
                using(var installLock=new FileStream(Path.Combine(root,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    // Keep this unique path stable: npm/Bun or a scanner may hold directory handles.
                    // A directory name is not proof of completion. Only return it after committing the marker.
                    Directory.CreateDirectory(stage);
                    report("install.node");
                    var nodeVersion=SelectNode(await platform.ReadAsync("https://nodejs.org/dist/index.json",token).ConfigureAwait(false));
                    var name="node-"+nodeVersion+"-win-x64.zip";var baseUrl="https://nodejs.org/dist/"+nodeVersion+"/";
                    var sums=await platform.ReadAsync(baseUrl+"SHASUMS256.txt",token).ConfigureAwait(false);
                    var match=Regex.Match(sums,@"(?m)^([a-f0-9]{64})\s+"+Regex.Escape(name)+@"\r?$");
                    if(!match.Success)throw new InvalidDataException(L.M("install.metadata"));
                    var zip=Path.Combine(stage,"node.zip");await platform.DownloadAsync(baseUrl+name,zip,token).ConfigureAwait(false);Verify(zip,match.Groups[1].Value,false);
                    var nodeDir=Path.Combine(stage,"node");ExtractNode(zip,nodeDir,token);
                    var node=Path.Combine(nodeDir,"node.exe");var home=Path.Combine(stage,"install-home");
                    var nodeOutput=await platform.RunAsync(node,new[]{"--version"},stage,home,token).ConfigureAwait(false);
                    if(nodeOutput.Trim()!=nodeVersion)throw new InvalidDataException(L.M("install.versionMismatch"));
                    report("install.package");
                    var tarball=Path.Combine(stage,"opencodex.tgz");await platform.DownloadAsync(release.Tarball,tarball,token).ConfigureAwait(false);Verify(tarball,release.Integrity,true);
                    var app=Path.Combine(stage,"app");Directory.CreateDirectory(app);File.WriteAllText(Path.Combine(app,"package.json"),"{\"private\":true}");
                    var npm=Path.Combine(nodeDir,"node_modules","npm","bin","npm-cli.js");
                    report("install.dependencies");
                    await platform.RunAsync(node,new[]{npm,"install","--ignore-scripts","--no-audit","--no-fund","--engine-strict","--registry=https://registry.npmjs.org/","--prefix",app,"--",tarball},app,home,token).ConfigureAwait(false);
                    // Only Bun's required runtime installer is executed; other dependency lifecycle scripts stay disabled.
                    var bun=Path.Combine(app,"node_modules","bun","install.js");
                    await platform.RunAsync(node,new[]{bun},Path.GetDirectoryName(bun),home,token).ConfigureAwait(false);
                    var relative=Path.Combine("app","node_modules","@bitkyc08","opencodex","bin","ocx.mjs");var entry=Path.Combine(stage,relative);
                    report("install.verify");
                    if(!File.Exists(entry)||ReadInstalledVersion(entry)!=release.Version)throw new InvalidDataException(L.M("install.versionMismatch"));
                    var output=await platform.RunAsync(node,new[]{entry,"--version"},app,home,token).ConfigureAwait(false);
                    if(!Regex.IsMatch(output,@"(?<![\d.])"+Regex.Escape(release.Version)+@"(?![\w.+-])"))throw new InvalidDataException(L.M("install.versionMismatch"));
                    token.ThrowIfCancellationRequested();
                    report("install.complete");
                    token.ThrowIfCancellationRequested();
                    TextFile.AtomicWrite(Path.Combine(stage,"installation.json"),JsonData.Serializer().Serialize(new {version=release.Version,node=nodeVersion,integrity=release.Integrity,installedUtc=DateTime.UtcNow.ToString("o")}),new UTF8Encoding(false));
                    return entry;
                }
            }
            catch(Exception error)
            {
                // Keep incomplete generations for diagnosis, never select them or touch the previous runtime.
                string log=null;
                try{var file=Path.Combine(stage,"failure.log");File.WriteAllText(file,"phase="+phase+Environment.NewLine+Redactor.Apply(error.ToString()));log=file;}catch{}
                if(error is OperationCanceledException)throw;
                var message=L.F("install.failureStage",L.M(phase))+"\n"+L.F("install.location",stage)+"\n"+Redactor.Apply(error.Message);
                if(error is UnauthorizedAccessException || (error.HResult & 0xffff)==5 || (error.HResult & 0xffff)==32)
                    message+="\n"+L.M("install.accessHint");
                message+="\n"+(log==null?L.M("install.noLog"):L.F("install.log",log));
                throw new IOException(message,error);
            }
        }
    }
}
