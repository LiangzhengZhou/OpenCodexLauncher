using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenCodexLauncherV2;

static class UpdaterChecks
{
    static string NewDirectory(string root,string name) { var path=Path.Combine(root,name+"-"+Guid.NewGuid().ToString("N").Substring(0,12));Directory.CreateDirectory(path);return path; }
    static LauncherRelease Release(string version,byte[] zip)
    {return new LauncherRelease {Version=version,Size=zip.Length,Sha256=LauncherUpdater.Hash(zip),Url="https://github.com/"+LauncherUpdater.Repository+"/releases/download/v"+version+"/OpenCodexLauncher-"+version+"-windows-x64.zip"};}
    static string Metadata(LauncherRelease release,bool draft=false,bool prerelease=false,int count=1)
    {return JsonData.Serializer().Serialize(new {tag_name="v"+release.Version,draft=draft,prerelease=prerelease,assets=Enumerable.Range(0,count).Select(x=>new {name="OpenCodexLauncher-"+release.Version+"-windows-x64.zip",size=release.Size,digest="sha256:"+release.Sha256,browser_download_url=release.Url}).ToArray()});}
    static bool Refuses(Action action) {try{action();return false;}catch{return true;}}
    static async Task<bool> RefusesAsync(Func<Task> action) {try{await action();return false;}catch{return true;}}
    static byte[] Package(string root,string executable)
    {
        var directory=NewDirectory(root,"package");
        foreach(var name in LauncherUpdater.Files.Where(x=>x!="SHA256SUMS.txt"))
        {if(name=="OpenCodexLauncher.exe")File.Copy(executable,Path.Combine(directory,name));else File.WriteAllText(Path.Combine(directory,name),name=="OpenCodexLauncher.exe.config"?"<?xml version=\"1.0\"?><configuration/>":"fixture "+name);}
        File.WriteAllLines(Path.Combine(directory,"SHA256SUMS.txt"),LauncherUpdater.Files.Where(x=>x!="SHA256SUMS.txt").Select(x=>LauncherUpdater.FileHash(Path.Combine(directory,x))+"  "+x));
        var zip=directory+".zip";ZipFile.CreateFromDirectory(directory,zip);return File.ReadAllBytes(zip);
    }
    static LauncherUpdatePlan Plan(string target,LauncherRelease release)
    {return new LauncherUpdatePlan {Target=target,Release=release,CurrentVersion="2.5.2",Before=LauncherUpdater.Files.ToDictionary(x=>x,x=>LauncherUpdater.FileHash(Path.Combine(target,x)))};}
    static string Target(string root)
    {
        var target=NewDirectory(root,"installed");
        foreach(var name in LauncherUpdater.Files)File.WriteAllText(Path.Combine(target,name),"previous "+name);
        File.WriteAllText(Path.Combine(target,"user-notes.txt"),"preserve fixture");return target;
    }
    static string Extract(string root,byte[] bytes,LauncherRelease release)
    {var baseDir=NewDirectory(root,"extract");var zip=Path.Combine(baseDir,"package.zip");File.WriteAllBytes(zip,bytes);var payload=Path.Combine(baseDir,"payload");LauncherUpdater.VerifyPackage(zip,payload,release);return payload;}
    public static async Task Run(string root,Action<bool,string> check)
    {
        root=NewDirectory(root,"updater"); var current=LauncherUpdater.CurrentVersion;var next="9.0.0";
        var release=Release(next,new byte[]{1});
        check(LauncherUpdater.ParseRelease(Metadata(release),current).Version==next,"launcher updater selects a newer stable release");
        check(LauncherUpdater.ParseRelease(Metadata(Release(current,new byte[]{1})),current)==null && LauncherUpdater.ParseRelease(Metadata(Release("1.0.0",new byte[]{1})),current)==null,"launcher updater rejects equal versions and downgrades");
        check(Refuses(()=>LauncherUpdater.ParseRelease(Metadata(release,true),current)) && Refuses(()=>LauncherUpdater.ParseRelease(Metadata(release,false,true),current)),"launcher updater refuses drafts and prereleases");
        check(Refuses(()=>LauncherUpdater.ParseRelease(Metadata(release,false,false,0),current)) && Refuses(()=>LauncherUpdater.ParseRelease(Metadata(release,false,false,2),current)),"launcher updater requires exactly one matching Windows asset");
        foreach(var version in new[]{"1.2.3-beta","1.2.3.4","01.2.3","../2.0.0","999999999.0.0"})check(Refuses(()=>LauncherUpdater.ParseVersion(version)),"invalid launcher version rejected: "+version);
        foreach(var url in new[]{"http://github.com/","https://github.com.evil.invalid/","https://user@github.com/","https://github.com/other/repo/releases/download/v1/a.zip","https://release-assets.githubusercontent.com:444/"})
            check(!LauncherUpdater.AllowedUri(new Uri(url)),"unsafe update origin rejected");
        var bad=Release(next,new byte[]{1});bad.Sha256="";check(Refuses(()=>LauncherUpdater.ValidateRelease(bad)),"missing GitHub SHA256 digest rejected");
        bad=Release(next,new byte[]{1});bad.Url="https://example.invalid/a.zip";check(Refuses(()=>LauncherUpdater.ValidateRelease(bad)),"wrong repository asset rejected");
        int requests=0;
        var updater=new LauncherUpdater((uri,limit,token)=>{requests++;return Task.FromResult(Encoding.UTF8.GetBytes(Metadata(release)));});
        check(requests==0,"constructing launcher updater performs no networking");
        using(var cancelled=new CancellationTokenSource()) {cancelled.Cancel();check(await RefusesAsync(async()=>{await updater.CheckAsync(cancelled.Token);}) && requests==0,"cancelled launcher check sends no request");}
        check((await updater.CheckAsync(CancellationToken.None)).Version==next && requests==1,"explicit launcher check uses injectable metadata transport");
        var bytes=Package(root,typeof(LauncherUpdater).Assembly.Location);release=Release(current,bytes);
        var payload=Extract(root,bytes,release);check(File.Exists(Path.Combine(payload,LauncherUpdater.Files[0])),"valid launcher package validates checksums and assembly version");
        var target=Target(root);var plan=Plan(target,release);
        var recovery=await LauncherUpdater.ApplyAsync(plan,payload,null);
        check(LauncherUpdater.FileHash(Path.Combine(target,LauncherUpdater.Files[0]))==LauncherUpdater.FileHash(typeof(LauncherUpdater).Assembly.Location) && File.ReadAllText(Path.Combine(target,"user-notes.txt"))=="preserve fixture","launcher update replaces release files and preserves unrelated user files");
        check(File.ReadAllText(Path.Combine(recovery,"journal.json")).Contains("committed"),"successful launcher update retains a committed backup journal");
        target=Target(root);plan=Plan(target,release);
        check(await RefusesAsync(()=>LauncherUpdater.ApplyAsync(plan,payload,i=>{if(i==2)throw new IOException("injected copy failure");})) && plan.Before.All(x=>LauncherUpdater.FileHash(Path.Combine(target,x.Key))==x.Value),"partial launcher replacement failure restores every original file");
        target=NewDirectory(root,"minimal-install");File.WriteAllText(Path.Combine(target,LauncherUpdater.Files[0]),"old");plan=Plan(target,release);
        check(await RefusesAsync(()=>LauncherUpdater.ApplyAsync(plan,payload,i=>{if(i==3)throw new IOException("injected failure");})) && Directory.GetFiles(target).Length==1 && File.ReadAllText(Path.Combine(target,LauncherUpdater.Files[0]))=="old","rollback removes newly introduced release files");
        target=Target(root);plan=Plan(target,release);File.WriteAllText(Path.Combine(target,"README.md"),"external edit");
        check(await RefusesAsync(()=>LauncherUpdater.ApplyAsync(plan,payload,null)) && File.ReadAllText(Path.Combine(target,"README.md"))=="external edit","launcher update refuses changes after staging");
        target=Target(root);plan=Plan(target,release);
        check(await RefusesAsync(()=>LauncherUpdater.ApplyAsync(plan,payload,i=>{if(i==1)File.WriteAllText(Path.Combine(target,"README.md"),"external during update");})) && File.ReadAllText(Path.Combine(target,"README.md"))=="external during update","failed rollback preserves concurrent external edits and recovery journal");
        target=Target(root);File.SetAttributes(Path.Combine(target,"README.md"),FileAttributes.ReadOnly);
        check(Refuses(()=>LauncherUpdater.CheckTarget(target)),"read-only installation is rejected before restart");File.SetAttributes(Path.Combine(target,"README.md"),FileAttributes.Normal);
        var zipPath=Path.Combine(root,"bad-hash.zip");File.WriteAllBytes(zipPath,bytes);bad=Release(current,bytes);bad.Sha256=new string('0',64);
        check(Refuses(()=>LauncherUpdater.VerifyPackage(zipPath,Path.Combine(root,"bad-hash"),bad)),"tampered launcher ZIP rejected before extraction");
        bad=Release("9.0.0",bytes);check(Refuses(()=>LauncherUpdater.VerifyPackage(zipPath,Path.Combine(root,"bad-version"),bad)),"assembly version must match release tag");
        foreach(var name in new[]{"../escaped.txt","config.json","README.MD"})
        {
            using(var memory=new MemoryStream())
            {
                using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))foreach(var file in LauncherUpdater.Files)using(var writer=new StreamWriter(zip.CreateEntry(file=="README.md"?name:file).Open()))writer.Write("fixture");
                var malformed=memory.ToArray();var path=Path.Combine(root,"malformed-"+Guid.NewGuid().ToString("N")+".zip");File.WriteAllBytes(path,malformed);
                check(Refuses(()=>LauncherUpdater.VerifyPackage(path,path+"-out",Release(current,malformed))),"unlisted or traversal archive entry rejected");
            }
        }
        File.WriteAllText(Path.Combine(payload,"README.md"),"tampered payload");
        check(Refuses(()=>LauncherUpdater.VerifyStage(payload,current)),"payload tampering is caught by internal checksums");
        await HelperCheck(root,check);
    }
    static void Compile(string root,string output,string version,string body)
    {
        var source=Path.Combine(root,Guid.NewGuid().ToString("N")+".cs");
        File.WriteAllText(source,"using System;using System.IO;using System.Diagnostics;using System.Threading;[assembly:System.Reflection.AssemblyVersion(\""+version+".0\")][assembly:System.Reflection.AssemblyFileVersion(\""+version+".0\")]class Fixture{static void Main(string[] args){"+body+"}}");
        var csc=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Microsoft.NET","Framework64","v4.0.30319","csc.exe");
        using(var p=Process.Start(new ProcessStartInfo(csc,"/nologo /target:winexe /platform:x64 /out:"+Commands.Quote(output)+" "+Commands.Quote(source)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}))
        {var a=p.StandardOutput.ReadToEndAsync();var b=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(20000)||p.ExitCode!=0)throw new Exception("update fixture compilation failed");Task.WaitAll(a,b);}
    }
    static async Task HelperCheck(string root,Action<bool,string> check)
    {
        var target=NewDirectory(root,"helper-install");var next=NewDirectory(root,"helper-next");
        var version=new Version(LauncherUpdater.CurrentVersion);var nextVersion=version.Major+"."+version.Minor+"."+(version.Build+1);
        Compile(root,Path.Combine(next,LauncherUpdater.Files[0]),nextVersion,"File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,\"restarted.txt\"),\"ok\");");
        Compile(root,Path.Combine(target,LauncherUpdater.Files[0]),LauncherUpdater.CurrentVersion,"var here=AppDomain.CurrentDomain.BaseDirectory;File.WriteAllText(Path.Combine(here,\"parent-ready.txt\"),\"ok\");for(int i=0;i<300&&!File.Exists(Path.Combine(here,\"exit-parent\"));i++)Thread.Sleep(100);");
        var bytes=Package(root,Path.Combine(next,LauncherUpdater.Files[0]));var release=Release(nextVersion,bytes);
        var data=NewDirectory(Path.GetDirectoryName(root),"uh");
        var environment=LocalEnvironment.Current;string planPath;
        try
        {
            LocalEnvironment.Current=new LocalEnvironment(data,Path.Combine(data,"user"),true);
            int requests=0;
            var updater=new LauncherUpdater((uri,limit,token)=>{requests++;return Task.FromResult(bytes);});
            using(var cancellation=new CancellationTokenSource())
            {cancellation.Cancel();check(await RefusesAsync(async()=>{await updater.StageAsync(release,target,cancellation.Token);})&&requests==0,"cancelled staging leaves installed files unchanged without downloading");}
            var original=LauncherUpdater.FileHash(Path.Combine(target,LauncherUpdater.Files[0]));
            planPath=await updater.StageAsync(release,target,CancellationToken.None);
            check(requests==1 && File.Exists(Path.Combine(Path.GetDirectoryName(planPath),"update-helper.exe")) && LauncherUpdater.FileHash(Path.Combine(target,LauncherUpdater.Files[0]))==original,"download staging creates verified payload and helper without replacing the running installation");
            var cancelledDownload=new LauncherUpdater((uri,limit,token)=>{throw new OperationCanceledException();});
            check(await RefusesAsync(async()=>{await cancelledDownload.StageAsync(release,target,CancellationToken.None);})&&LauncherUpdater.FileHash(Path.Combine(target,LauncherUpdater.Files[0]))==original,"cancelled download preserves the installation");
            var damagedDownload=new LauncherUpdater((uri,limit,token)=>Task.FromResult(new byte[]{0}));
            check(await RefusesAsync(async()=>{await damagedDownload.StageAsync(release,target,CancellationToken.None);})&&LauncherUpdater.FileHash(Path.Combine(target,LauncherUpdater.Files[0]))==original,"damaged download is rejected before helper launch");
        }
        finally {LocalEnvironment.Current=environment;}
        var staging=Path.GetDirectoryName(planPath);
        using(var parent=Process.Start(new ProcessStartInfo(Path.Combine(target,LauncherUpdater.Files[0])){UseShellExecute=false,CreateNoWindow=true}))
        {
            Process helper=null;
            try
            {
                for(int i=0;i<100&&!File.Exists(Path.Combine(target,"parent-ready.txt"));i++)await Task.Delay(50);
                var plan=JsonData.Serializer().Deserialize<LauncherUpdatePlan>(File.ReadAllText(planPath));plan.ParentId=parent.Id;plan.ParentStart=parent.StartTime.ToUniversalTime().Ticks;plan.Language="en";
                File.WriteAllText(planPath,JsonData.Serializer().Serialize(plan));
                using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25)))helper=await LauncherUpdater.StartHelperAsync(planPath,timeout.Token);
                check(!parent.HasExited && !File.Exists(Path.Combine(target,"restarted.txt")),"real update helper waits for exact parent process without terminating it");
                File.WriteAllText(Path.Combine(target,"exit-parent"),"");
                for(int i=0;i<200&&!File.Exists(Path.Combine(target,"restarted.txt"));i++)await Task.Delay(100);
                check(File.Exists(Path.Combine(target,"restarted.txt")) && FileVersionInfo.GetVersionInfo(Path.Combine(target,LauncherUpdater.Files[0])).FileVersion==nextVersion+".0","real helper replaces an exited fixture EXE and starts the verified new version");
                check(helper.WaitForExit(5000) && helper.ExitCode==0 && File.Exists(Path.Combine(staging,"result.txt")),"real updater records success and exits cleanly");
            }
            finally{if(!parent.HasExited)AsyncProcessRunner.KillTree(parent.Id);if(helper!=null){if(!helper.HasExited)AsyncProcessRunner.KillTree(helper.Id);helper.Dispose();}}
        }
    }
}
