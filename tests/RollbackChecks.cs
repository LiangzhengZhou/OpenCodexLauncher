using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenCodexLauncherV2;

class RollbackChecks
{
    static int passed;
    static void Check(bool value,string name) { if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++; }
    static bool Refuses(Action action) {try{action();return false;}catch{return true;}}
    static async Task<bool> RefusesAsync(Func<Task> action) {try{await action();return false;}catch{return true;}}
    static int Main(string[] args) {try{Run(args[0],args[1]).GetAwaiter().GetResult();Console.WriteLine("ALL "+passed+" ROLLBACK CHECKS PASSED");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
    static async Task Run(string zip,string root)
    {
        Directory.CreateDirectory(root);LocalEnvironment.UseIsolated(root);
        var selected=CriticalVersions.Find("2.6.5");
        Check(CriticalVersions.All().Length==1 && Refuses(()=>CriticalVersions.Find("3.0.0")),"only confirmed milestones selectable");
        CriticalVersions.All()[0].Sha256="changed";
        Check(CriticalVersions.Find("2.6.5").Sha256==selected.Sha256,"callers cannot mutate the milestone policy");
        Check(Refuses(()=>LauncherUpdater.ValidateTransition(selected,LauncherUpdater.CurrentVersion,false)),"ordinary updater still refuses downgrade");
        Check(Refuses(()=>CriticalVersions.Validate(selected,"2.6.5")),"rollback refuses current version");
        var changed=CriticalVersions.Find("2.6.5");changed.Sha256=new string('0',64);
        Check(Refuses(()=>CriticalVersions.Validate(changed,LauncherUpdater.CurrentVersion)),"rollback refuses modified milestone digest");
        var settings=new LauncherSettings {SettingsVersion=1,Language="en",ConfigurationMode="manual",SetupCompleted=true,LastSelectedModel="demo-provider/model",DesktopConfigPath=Path.Combine(root,"desktop","config.toml"),OcxPath=Path.Combine(root,"runtime","ocx.mjs")};
        Directory.CreateDirectory(Path.GetDirectoryName(settings.DesktopConfigPath));File.WriteAllText(settings.DesktopConfigPath,"model = \"demo-provider/model\"\n");
        PathResolver.Save(settings);var settingsFile=PathResolver.SettingsPath();var originalSettings=File.ReadAllBytes(settingsFile);
        settings.ReserveForceEnabled=true;PathResolver.Save(settings);
        Check(Refuses(()=>CriticalVersions.CheckConfiguration()),"Reserve Force blocks rollback before downloading");
        File.WriteAllText(settingsFile,"{\"SettingsVersion\":99}");
        Check(Refuses(()=>CriticalVersions.CheckConfiguration()),"newer settings schema blocks rollback without migration");
        File.WriteAllText(settingsFile,"broken");Check(Refuses(()=>CriticalVersions.CheckConfiguration()),"corrupt settings block rollback");
        File.WriteAllBytes(settingsFile,originalSettings);
        var provider=PathResolver.Resolve(settings).OcxConfig;Directory.CreateDirectory(Path.GetDirectoryName(provider));
        File.WriteAllText(provider,"{\"providers\":{\"demo-provider\":{\"adapter\":\"openai-chat\",\"baseUrl\":\"https://example.invalid/v1\",\"selectedModels\":[\"model\"],\"models\":[\"model\"]}}}");
        CredentialStore.Save("demo-provider","dummy-rollback-credential");
        var credential=CredentialStore.PathFor("demo-provider");
        var privateFiles=new[]{settingsFile,provider,credential,settings.DesktopConfigPath}.ToDictionary(x=>x,LauncherUpdater.FileHash);
        var payload=Path.Combine(root,"historical-payload");LauncherUpdater.VerifyPackage(zip,payload,selected);
        Check(File.Exists(Path.Combine(payload,"OpenCodexLauncher.exe")),"real 2.6.5 release validates pinned hash, allowlist and assembly version");
        var legacy=System.Reflection.Assembly.LoadFile(Path.GetFullPath(Path.Combine(payload,"OpenCodexLauncher.exe")));
        var legacyEnvironment=legacy.GetType("OpenCodexLauncherV2.LocalEnvironment");
        legacyEnvironment.GetField("Current").SetValue(null,Activator.CreateInstance(legacyEnvironment,new object[]{LocalEnvironment.Current.DataDirectory,LocalEnvironment.Current.UserDirectory,true}));
        var legacySettings=legacy.GetType("OpenCodexLauncherV2.PathResolver").GetMethod("Load").Invoke(null,null);
        Check((string)legacySettings.GetType().GetProperty("LastSelectedModel").GetValue(legacySettings,null)==settings.LastSelectedModel &&
            (string)legacySettings.GetType().GetProperty("DesktopConfigPath").GetValue(legacySettings,null)==settings.DesktopConfigPath &&
            (string)legacySettings.GetType().GetProperty("OcxPath").GetValue(legacySettings,null)==settings.OcxPath,"actual 2.6.5 reader preserves selected model, Desktop association and runtime path");
        Check((string)legacy.GetType("OpenCodexLauncherV2.CredentialStore").GetMethod("Load").Invoke(null,new object[]{"demo-provider"})=="dummy-rollback-credential","actual 2.6.5 decrypts current DPAPI credentials in isolated storage");
        var currentPayload=Path.Combine(root,"current-payload");Directory.CreateDirectory(currentPayload);
        foreach(var name in LauncherUpdater.Files.Where(x=>x!="SHA256SUMS.txt"))
        {if(name=="OpenCodexLauncher.exe")File.Copy(typeof(LauncherUpdater).Assembly.Location,Path.Combine(currentPayload,name));else File.Copy(Path.Combine(payload,name),Path.Combine(currentPayload,name));}
        File.WriteAllLines(Path.Combine(currentPayload,"SHA256SUMS.txt"),LauncherUpdater.Files.Where(x=>x!="SHA256SUMS.txt").Select(x=>LauncherUpdater.FileHash(Path.Combine(currentPayload,x))+"  "+x));
        var target=Path.Combine(root,"installation");Directory.CreateDirectory(target);
        foreach(var name in LauncherUpdater.Files)File.Copy(Path.Combine(currentPayload,name),Path.Combine(target,name));
        File.WriteAllText(Path.Combine(target,"unrelated.txt"),"preserve");
        int requests=0;var bytes=File.ReadAllBytes(zip);
        var updater=new LauncherUpdater((uri,limit,token)=>{requests++;return Task.FromResult(bytes);});
        var before=LauncherUpdater.Files.ToDictionary(x=>x,x=>LauncherUpdater.FileHash(Path.Combine(target,x)));
        using(var cancelled=new CancellationTokenSource()) {cancelled.Cancel();Check(await RefusesAsync(async()=>{await updater.StageRollbackAsync("2.6.5",target,cancelled.Token);})&&requests==0,"cancelled rollback does not download");}
        var planFile=await updater.StageRollbackAsync("2.6.5",target,CancellationToken.None);
        var plan=JsonData.Serializer().Deserialize<LauncherUpdatePlan>(File.ReadAllText(planFile));
        Check(plan.IsRollback && requests==1 && plan.UserDirectory==LocalEnvironment.Current.UserDirectory && plan.IsIsolated && before.All(x=>LauncherUpdater.FileHash(Path.Combine(target,x.Key))==x.Value),"staging explicit rollback preserves configuration context and leaves installation intact");
        var offline=new LauncherUpdater((uri,limit,token)=>{throw new IOException("offline fixture");});
        Check(!String.IsNullOrEmpty(await offline.StageRollbackAsync("2.6.5",target,CancellationToken.None)),"verified cached milestone works offline");
        var cache=CriticalVersions.CachePath(selected);File.WriteAllText(cache,"damaged");
        Check(await RefusesAsync(async()=>{await updater.StageRollbackAsync("2.6.5",target,CancellationToken.None);})&&requests==1,"damaged cache stops instead of bypassing checksum");
        File.WriteAllBytes(cache,bytes);
        Check(await RefusesAsync(()=>LauncherUpdater.ApplyAsync(plan,payload,i=>{if(i==2)throw new IOException("injected interruption");}))&&before.All(x=>LauncherUpdater.FileHash(Path.Combine(target,x.Key))==x.Value),"interrupted real downgrade restores every previous program file");
        Check(await RefusesAsync(()=>LauncherUpdater.ApplyAsync(plan,payload,null,()=>{throw new IOException("injected start failure");}))&&before.All(x=>LauncherUpdater.FileHash(Path.Combine(target,x.Key))==x.Value),"start failure restores previous application files");
        using(var held=new FileStream(Path.Combine(target,"README.md"),FileMode.Open,FileAccess.Read,FileShare.Read))
            Check(await RefusesAsync(()=>LauncherUpdater.ApplyAsync(plan,payload,null))&&before.All(x=>LauncherUpdater.FileHash(Path.Combine(target,x.Key))==x.Value),"file lock blocks downgrade and restores prior files");
        File.WriteAllText(Path.Combine(target,"README.md"),"external edit");
        Check(await RefusesAsync(()=>LauncherUpdater.ApplyAsync(plan,payload,null))&&File.ReadAllText(Path.Combine(target,"README.md"))=="external edit","external edits after staging are not overwritten");
        File.Copy(Path.Combine(currentPayload,"README.md"),Path.Combine(target,"README.md"),true);
        var recovery=await LauncherUpdater.ApplyAsync(plan,payload,null);
        Check(File.Exists(Path.Combine(recovery,"journal.json"))&&File.ReadAllText(Path.Combine(recovery,"journal.json")).Contains("committed"),"successful downgrade records local recovery snapshot");
        Check(System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(target,"OpenCodexLauncher.exe")).Version.ToString()=="2.6.5.0","selected 2.6.5 binary actually replaces the newer installation");
        Check(privateFiles.All(x=>LauncherUpdater.FileHash(x.Key)==x.Value)&&File.ReadAllText(Path.Combine(target,"unrelated.txt"))=="preserve","settings, provider data, credentials and unrelated files remain byte-identical");
        // Switch the actual historical program files back to the current build.
        var current=new LauncherRelease {Version=LauncherUpdater.CurrentVersion,Size=1,Sha256=new string('0',64),Url="https://github.com/"+LauncherUpdater.Repository+"/releases/download/v"+LauncherUpdater.CurrentVersion+"/OpenCodexLauncher-"+LauncherUpdater.CurrentVersion+"-windows-x64.zip"};
        await LauncherUpdater.ApplyAsync(new LauncherUpdatePlan {Release=current,CurrentVersion="2.6.5",Target=target,Before=LauncherUpdater.Files.ToDictionary(x=>x,x=>LauncherUpdater.FileHash(Path.Combine(target,x)))},currentPayload,null);
        Check(System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(target,"OpenCodexLauncher.exe")).Version.ToString(3)==LauncherUpdater.CurrentVersion&&privateFiles.All(x=>LauncherUpdater.FileHash(x.Key)==x.Value),"current to 2.6.5 to current preserves private configuration");
    }
}
