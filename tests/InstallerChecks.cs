using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCodexLauncherV2;

public sealed class FakeInstallerPlatform : IInstallerPlatform
{
    public byte[] Zip;
    public byte[] Package=Encoding.UTF8.GetBytes("fictional test package");
    public bool BadHash,FailNpm,BadVersion,FailDownload;
    public int Calls,Runs;
    public CancellationTokenSource CancelAtNpm;
    public TaskCompletionSource<bool> PauseDownload;
    public string Version="3.2.1";
    public FakeInstallerPlatform()
    {
        using(var memory=new MemoryStream())
        {
            using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))
                foreach(var name in new[]{"node-v24.0.0-win-x64/node.exe","node-v24.0.0-win-x64/node_modules/npm/bin/npm-cli.js"})
                    using(var stream=new StreamWriter(zip.CreateEntry(name).Open()))stream.Write("fixture");
            Zip=memory.ToArray();
        }
    }
    public string Metadata()
    {
        using(var sha=SHA512.Create())return JsonData.Serializer().Serialize(new {name="@bitkyc08/opencodex",version=Version,dist=new {
            tarball="https://registry.npmjs.org/@bitkyc08/opencodex/-/opencodex-"+Version+".tgz",integrity="sha512-"+Convert.ToBase64String(sha.ComputeHash(Package))}});
    }
    public Task<string> ReadAsync(string url,CancellationToken token)
    {
        Calls++;token.ThrowIfCancellationRequested();
        if(url.Contains("index.json"))return Task.FromResult("[{\"version\":\"v24.0.0\",\"lts\":\"Example\",\"files\":[\"win-x64-zip\"]}]");
        if(url.Contains("SHASUMS"))using(var sha=SHA256.Create())return Task.FromResult(BitConverter.ToString(sha.ComputeHash(Zip)).Replace("-","").ToLowerInvariant()+"  node-v24.0.0-win-x64.zip\n");
        return Task.FromResult(Metadata());
    }
    public async Task DownloadAsync(string url,string file,CancellationToken token)
    {
        Calls++;token.ThrowIfCancellationRequested();
        if(PauseDownload!=null)await PauseDownload.Task;
        token.ThrowIfCancellationRequested();if(FailDownload)throw new IOException("simulated disconnected download");
        File.WriteAllBytes(file,BadHash?new byte[]{1,2,3}:url.EndsWith(".zip")?Zip:Package);
    }
    public Task<string> RunAsync(string executable,string[] args,string directory,string home,CancellationToken token)
    {
        Runs++;token.ThrowIfCancellationRequested();
        if(args[0]=="--version")return Task.FromResult("v24.0.0\n");
        if(args.Contains("install"))
        {
            if(CancelAtNpm!=null){CancelAtNpm.Cancel();token.ThrowIfCancellationRequested();}
            if(FailNpm)throw new IOException("simulated npm failure");
            if(!args.Contains("--ignore-scripts") || !args.Contains("--engine-strict") || !args.Contains("--registry=https://registry.npmjs.org/"))throw new Exception("unsafe npm arguments");
            var package=Path.Combine(directory,"node_modules","@bitkyc08","opencodex");Directory.CreateDirectory(Path.Combine(package,"bin"));
            File.WriteAllText(Path.Combine(package,"package.json"),"{\"name\":\"@bitkyc08/opencodex\",\"version\":\""+Version+"\"}");
            File.WriteAllText(Path.Combine(package,"bin","ocx.mjs"),"fixture");
            var bun=Path.Combine(directory,"node_modules","bun");Directory.CreateDirectory(bun);File.WriteAllText(Path.Combine(bun,"install.js"),"fixture");
        }
        return Task.FromResult(args.Last()=="--version"?(BadVersion?"0.0.1":Version):"");
    }
}

static class InstallerChecks
{
    static async Task<bool> Fails(Func<Task> action){try{await action();return false;}catch{return true;}}
    static bool Reject(Action action){try{action();return false;}catch{return true;}}
    public static async Task Run(string root,Action<bool,string> check)
    {
        var fake=new FakeInstallerPlatform();var release=OpenCodexRelease.Parse(fake.Metadata());
        check(release.Version=="3.2.1"&&release.IsNewerThan("3.2.0")&&!release.IsNewerThan("3.2.1")&&!release.IsNewerThan("3.10.0"),"stable version comparison prevents equal installs and downgrades");
        check(Reject(()=>OpenCodexRelease.Parse(fake.Metadata().Replace("3.2.1","3.2.1-preview.1"))),"latest metadata rejects prereleases");
        check(Reject(()=>OpenCodexRelease.Parse(fake.Metadata().Replace("sha512-","sha256-"))),"package metadata requires SHA512 integrity");
        check(Reject(()=>InstallerPlatform.TrustedUri("http://nodejs.org/dist/a.zip"))&&Reject(()=>InstallerPlatform.TrustedUri("https://nodejs.org.example.invalid/a.zip"))&&Reject(()=>InstallerPlatform.TrustedUri("https://user@nodejs.org/a.zip")),"download origin checks reject HTTP, spoofed hosts and userinfo");
        var index="[{\"version\":\"v26.0.0\",\"lts\":false,\"files\":[\"win-x64-zip\"]},{\"version\":\"v22.10.0\",\"lts\":\"Example\",\"files\":[\"win-x64-zip\"]},{\"version\":\"v24.1.0\",\"lts\":\"Example\",\"files\":[\"win-x64-zip\"]}]";
        check(OpenCodexInstaller.SelectNode(index)=="v24.1.0","Node selection uses latest numeric LTS with Windows x64 zip");
        check(Reject(()=>OpenCodexInstaller.SelectNode("[]")),"missing supported Node runtime fails explicitly");
        Environment.SetEnvironmentVariable("NPM_TOKEN","dummy-not-for-child");Environment.SetEnvironmentVariable("NODE_OPTIONS","dummy-not-for-child");
        var info=InstallerPlatform.ProcessInfo(Path.Combine(root,"node.exe"),new[]{"--version"},root,Path.Combine(root,"clean-home"));
        check(!info.EnvironmentVariables.ContainsKey("NPM_TOKEN")&&!info.EnvironmentVariables.ContainsKey("NODE_OPTIONS")&&info.EnvironmentVariables["OPENCODEX_HOME"].StartsWith(root)&&!info.UseShellExecute&&info.CreateNoWindow,"installer processes use empty homes without inherited tokens or preload options");
        Environment.SetEnvironmentVariable("NPM_TOKEN",null);Environment.SetEnvironmentVariable("NODE_OPTIONS",null);
        var runtimeRoot=Path.Combine(LocalEnvironment.Current.DataDirectory,"runtimes");var service=new OpenCodexInstaller(runtimeRoot,fake);
        check(fake.Calls==0&&!Directory.Exists(runtimeRoot),"constructing installer does not read environment or access network");
        var settingsHash=FileTransaction.Hash(PathResolver.SettingsPath());
        var stages=new List<string>();var entry=await service.InstallAsync(release,stages.Add,CancellationToken.None);
        check(File.Exists(entry)&&OpenCodexInstaller.ReadInstalledVersion(entry)=="3.2.1"&&stages.Last()=="install.verify","successful installer validates package and CLI in a new generation");
        check(OpenCodexInstaller.ManagedNode(entry)!=null&&Commands.Ocx(entry,"--version").File==OpenCodexInstaller.ManagedNode(entry),"managed runtime starts with private Node without system Node");
        check(FileTransaction.Hash(PathResolver.SettingsPath())==settingsHash,"install service never changes settings before explicit selection");
        var old=new LauncherSettings { OcxPath=entry,ConfigurationMode="imported",WorkingDirectory="draft",Language="zh",LastSelectedModel="demo-provider/example",SetupCompleted=true };
        var next=RuntimeSelection.Create(old,entry,"new-entry",false);
        check(old.OcxPath==entry&&next.PreviousOcxPath==entry&&next.WorkingDirectory==old.WorkingDirectory&&next.ConfigurationMode=="imported"&&next.Language=="zh"&&next.LastSelectedModel==old.LastSelectedModel,"runtime selection copies settings and preserves imported configuration and drafts");
        next=RuntimeSelection.Create(new LauncherSettings(),null,entry,true);
        check(next.SetupCompleted&&next.ConfigurationMode=="manual"&&PathResolver.Resolve(next).OcxConfig.StartsWith(LocalEnvironment.Current.DataDirectory),"one-click setup selects empty manual homes");
        old.ReserveForceEnabled=true;check(Reject(()=>RuntimeSelection.Create(old,entry,"new",false))&&old.OcxPath==entry,"Reserve Force prevents runtime switching before any change");
        foreach(var failure in new[]{"hash","download","npm","version","cancel"})
        {
            var bad=new FakeInstallerPlatform {BadHash=failure=="hash",FailDownload=failure=="download",FailNpm=failure=="npm",BadVersion=failure=="version"};
            using(var cancel=new CancellationTokenSource())
            {
                if(failure=="cancel")bad.CancelAtNpm=cancel;
                var before=Directory.GetDirectories(runtimeRoot,"ocx-*").Length;
                check(await Fails(async()=>{await new OpenCodexInstaller(runtimeRoot,bad).InstallAsync(release,key=>{},cancel.Token);})&&Directory.GetDirectories(runtimeRoot,"ocx-*").Length==before&&File.Exists(entry)&&FileTransaction.Hash(PathResolver.SettingsPath())==settingsHash,"failed "+failure+" leaves selected settings and previous runtime intact");
                if(failure=="hash")check(bad.Runs==0,"hash mismatch executes no downloaded code");
            }
        }
        var held=new FakeInstallerPlatform {PauseDownload=new TaskCompletionSource<bool>()};
        var first=new OpenCodexInstaller(runtimeRoot,held).InstallAsync(release,key=>{},CancellationToken.None);
        check(await Fails(async()=>{await service.InstallAsync(release,key=>{},CancellationToken.None);}),"install lock prevents concurrent launcher instances mutating installation state");
        held.PauseDownload.SetResult(true);await first;
        var malicious=Path.Combine(root,"traversal.zip");using(var stream=File.Create(malicious))using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("node/../../escape.txt").Open()))writer.Write("bad");
        check(Reject(()=>OpenCodexInstaller.ExtractNode(malicious,Path.Combine(root,"extract"),CancellationToken.None))&&!File.Exists(Path.Combine(root,"escape.txt")),"ZIP path traversal is rejected");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();var before=fake.Calls;check(await Fails(async()=>{await service.InstallAsync(release,key=>{},cancel.Token);})&&fake.Calls==before,"pre-cancelled installation has no network effects");}
        var pidFile=Path.Combine(root,"installer-child.pid");
        using(var cancel=new CancellationTokenSource())
        {
            var command=Process.GetCurrentProcess().MainModule.FileName;
            var task=new InstallerPlatform().RunAsync(command,new[]{"installer-parent",pidFile},root,Path.Combine(root,"process-home"),cancel.Token);
            var limit=DateTime.UtcNow.AddSeconds(20);while(!File.Exists(pidFile)&&!task.IsCompleted&&DateTime.UtcNow<limit)await Task.Delay(50);
            if(!File.Exists(pidFile)){if(task.IsCompleted)await task;cancel.Cancel();await Fails(async()=>{await task;});throw new Exception("installer process probe did not start");}
            using(var child=Process.GetProcessById(Int32.Parse(File.ReadAllText(pidFile))))
            {cancel.Cancel();var cancelled=await Fails(async()=>{await task;});check(cancelled&&child.WaitForExit(3000),"cancellation terminates installer child processes with the Windows job");}
        }
    }
}
