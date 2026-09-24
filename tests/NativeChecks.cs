using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using OpenCodexLauncherV2;
class NativeChecks
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); count++; }
    static string Bundle(string root, string name)
    {
        var directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "codex.exe"), "fixture only");
        File.WriteAllText(Path.Combine(directory, "codex-code-mode-host.exe"), "fixture only");
        return Path.Combine(directory, "codex.exe");
    }
    static void RuntimeChecks(string directory)
    {
        var root = Path.Combine(directory, "runtime-fixtures");
        var a = Bundle(root, "A"); var b = Bundle(root, "B");
        var manifest = new NativeBridgeSettings { RealCodex = a, CodexHome = "fixture-home", RoutesPath = "fixture-routes" };
        var manifestPath = Path.Combine(directory, "rollover-manifest.json");
        File.WriteAllText(manifestPath, JsonData.Serializer().Serialize(manifest));
        var activation = new NativeActivation(Path.Combine(directory, "rollover-activation.json"), key => key == "CODEX_CLI_PATH" ? b : manifestPath, (key, value) => { throw new Exception(); }, () => { });
        File.WriteAllText(Path.Combine(directory, "rollover-activation.json"), JsonData.Serializer().Serialize(new NativeActivationState { Helper = b, Manifest = manifestPath }));
        Check(activation.Enabled && activation.RuntimeHealth == "healthy", "activation registration and runtime health independently available");
        int scans = 0; Func<string,string> discover = r => { scans++; return CodexRuntime.FindUsable(r); };
        Check(CodexRuntime.ResolveNative(manifest, root, discover) == a && scans == 0, "complete manifest runtime does not scan or switch");
        File.Delete(Path.Combine(Path.GetDirectoryName(a), "codex-code-mode-host.exe"));
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddHours(1));
        Check(File.Exists(a) && !CodexRuntime.IsUsable(a) && CodexRuntime.IsUsable(b), "rollover leaves executable but removes required host");
        Check(activation.Enabled && activation.RuntimeHealth == "stale", "stale runtime does not erase activation registration");
        Check(CodexRuntime.FindUsable(root) == b, "newest incomplete executable excluded before ordering");
        Check(CodexRuntime.ResolveNative(manifest, root, discover) == b, "bridge startup resolution replaces stale A with complete B");
        var persisted = File.ReadAllText(manifestPath);
        Check(CodexRuntime.ResolveNative(JsonData.Serializer().Deserialize<NativeBridgeSettings>(persisted), root, discover) == b && File.ReadAllText(manifestPath) == persisted, "persisted stale manifest resolves without disk mutation");
        Check(JsonData.Serializer().Deserialize<NativeBridgeSettings>("{\"RealCodex\":\"fixture\",\"CodexHome\":\"home\",\"RoutesPath\":\"routes\"}").AutomaticRuntime == null, "3.1.0 manifest without provenance remains readable");
        Check(manifest.RealCodex == a && manifest.CodexHome == "fixture-home" && manifest.RoutesPath == "fixture-routes", "resolution leaves manifest and routing configuration untouched");
        File.Delete(a);
        Check(activation.RuntimeHealth == "missing", "missing runtime health is an allowlisted state");
        Check(CodexRuntime.ResolveNative(manifest, root, discover) == b, "missing executable also recovers");
        manifest.AutomaticRuntime = false; bool rejected = false;
        try { CodexRuntime.ResolveNative(manifest, root, discover); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "incomplete explicit runtime fails closed");
        manifest.RealCodex = b;
        Check(CodexRuntime.ResolveNative(manifest, root, r => { throw new Exception(); }) == b, "complete explicit runtime respected without discovery");
        var manual = Bundle(directory, "manual-runtime");
        manifest.RealCodex = manual; manifest.AutomaticRuntime = null;
        File.Delete(Path.Combine(Path.GetDirectoryName(manual), "codex-code-mode-host.exe"));
        rejected = false; try { CodexRuntime.ResolveNative(manifest, root, discover); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "legacy manual runtime outside managed root never substituted");
        manifest.RealCodex = a;
        File.Delete(Path.Combine(Path.GetDirectoryName(b), "codex-code-mode-host.exe"));
        rejected = false; try { CodexRuntime.ResolveNative(manifest, root, discover); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "no complete runtime fails closed without starting stale executable");
        b = Bundle(root, "B"); var c = Bundle(root, "C");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(b, stamp); File.SetLastWriteTimeUtc(c, stamp);
        Check(CodexRuntime.FindUsable(root) == b, "equal timestamp selection is deterministic");
        System.Threading.Tasks.Parallel.For(0, 12, i => { if (CodexRuntime.ResolveNative(manifest, root, CodexRuntime.FindUsable) != b) throw new Exception(); });
        Check(manifest.RealCodex == a, "concurrent startup resolution has no manifest writes");
        var selected = PathResolver.Resolve(new LauncherSettings { CodexPath = b, ConfigurationMode = "manual" });
        Check(selected.Codex == b && !selected.AutomaticCodexRuntime, "manual configured path priority preserved");
        Check(PathResolver.Resolve(new LauncherSettings()).Codex == null, "isolated discovery never scans real Desktop or PATH");
    }
    static void HelperUpdateChecks(string root, string sourceA, string sourceB, string manifest)
    {
        var env = new Dictionary<string,string>(); var state = Path.Combine(root,"update-cases.json");
        bool failSecond = false, failRollback = false; int writes = 0;
        var a = NativeActivation.InstallHelper(sourceA); var b = NativeActivation.InstallHelper(sourceB);
        var activation = new NativeActivation(state, k => env.ContainsKey(k) ? env[k] : null, (k,v) => {
            writes++;
            if (failSecond && k == "OPENCODEX_LAUNCHER_BRIDGE") { failSecond=false; throw new IOException("fixture write failure"); }
            if (failRollback && k == "CODEX_CLI_PATH" && v == a) throw new IOException("fixture rollback failure");
            if(v==null) env.Remove(k); else env[k]=v;
        }, () => {});
        Check(activation.CheckHelper(sourceB)=="disabled", "helper status detects disabled registration without writes");
        activation.Enable(a,manifest,()=>System.Threading.Tasks.Task.FromResult(0)).GetAwaiter().GetResult();
        var before=File.ReadAllText(state); var beforeWrites=writes;
        Check(activation.CheckHelper(sourceA)=="current" && activation.CheckHelper(sourceB)=="outdated" && writes==beforeWrites && File.ReadAllText(state)==before, "helper status compares bytes and is read only");
        var configSource=Path.Combine(root,"config-only.exe"); File.Copy(sourceA,configSource); File.WriteAllText(configSource+".config","changed config");
        Check(activation.CheckHelper(configSource)=="outdated", "config-only build change requires helper update");
        env["CODEX_CLI_PATH"]="external"; bool rejected=false;
        try { activation.UpdateHelper(sourceB); } catch(InvalidOperationException) { rejected=true; }
        Check(rejected && activation.CheckHelper(sourceB)=="conflict" && env["CODEX_CLI_PATH"]=="external", "external environment blocks helper update");
        env["CODEX_CLI_PATH"]=a;
        failSecond=true; rejected=false;
        try { activation.UpdateHelper(sourceB); } catch(IOException) { rejected=true; }
        Check(rejected && env["CODEX_CLI_PATH"]==a && File.ReadAllText(state)==before, "failed helper registration restores original record and environment");
        failSecond=true; failRollback=true; rejected=false;
        try { activation.UpdateHelper(sourceB); } catch(IOException) { rejected=true; }
        Check(rejected && JsonData.Serializer().Deserialize<NativeActivationState>(File.ReadAllText(state)).TransitionCli==a, "rollback failure retains recoverable transition record");
        failRollback=false;
        activation.UpdateHelper(sourceB);
        Check(activation.CheckHelper(sourceB)=="current", "retry after rollback failure recovers registration");
        activation.UpdateHelper(sourceA);
        Check(activation.CheckHelper(sourceA)=="current" && File.Exists(b), "returning to earlier build retains both helper bundles");
        using(var held=new FileStream(state+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            rejected=false; try { activation.UpdateHelper(sourceB); } catch(IOException) { rejected=true; }
            Check(rejected && env["CODEX_CLI_PATH"]==a,"concurrent registration is rejected without mutation");
        }
        System.Threading.Tasks.Parallel.For(0,8,i=> { if(NativeActivation.InstallHelper(configSource)!=NativeActivation.ExpectedHelperPath(configSource)) throw new Exception(); });
        Check(File.ReadAllBytes(NativeActivation.ExpectedHelperPath(configSource)).SequenceEqual(File.ReadAllBytes(configSource)), "concurrent content deployment produces complete bytes");
        File.WriteAllText(a,"external corruption");
        Check(activation.CheckHelper(sourceA)=="outdated", "matching hash directory does not hide corrupted helper bytes");
        rejected=false; try { activation.UpdateHelper(sourceA); } catch(IOException) { rejected=true; }
        Check(rejected && File.ReadAllText(a)=="external corruption", "deployment refuses to overwrite externally changed helper");
        File.Delete(a); File.Delete(a+".config");
        Check(activation.CheckHelper(sourceA)=="missing", "missing registered helper can be diagnosed");
        activation.UpdateHelper(sourceA);
        Check(activation.CheckHelper(sourceA)=="current", "missing registered helper repaired without Prepare");
        before=File.ReadAllText(state); File.Delete(configSource+".config"); rejected=false;
        try { activation.UpdateHelper(configSource); } catch(IOException) { rejected=true; }
        Check(rejected && File.ReadAllText(state)==before && env["CODEX_CLI_PATH"]==a, "incomplete source fails before registration mutation");
        activation.Disable();
        Check(env.Count==0,"disable after upgrades restores original environment");
    }
    static int Main(string[] args)
    {
        try {
            Directory.CreateDirectory(args[0]); LocalEnvironment.UseIsolated(args[0]);
            RuntimeChecks(args[0]);
            var exe = typeof(NativeProviders).Assembly.Location;
            var environment=new Dictionary<string,string>(); int broadcasts=0;
            var activation=new NativeActivation(Path.Combine(args[0],"activation.json"),
                key=>environment.ContainsKey(key)?environment[key]:null,
                (key,value)=>{if(value==null)environment.Remove(key);else environment[key]=value;},()=>broadcasts++);
            var installed=NativeActivation.InstallHelper(exe);
            Check(installed!=exe&&File.Exists(installed+".config")&&NativeActivation.InstallHelper(exe)==installed,"stable helper bundle installed idempotently");
            activation.Enable(installed,"fixture-manifest",()=>System.Threading.Tasks.Task.FromResult(0)).GetAwaiter().GetResult();
            Check(activation.Enabled&&environment["CODEX_CLI_PATH"]==installed&&broadcasts==1,"activation persists environment and broadcasts");
            var helperA=Path.Combine(args[0],"helper-a.exe"); var helperB=Path.Combine(args[0],"helper-b.exe");
            File.WriteAllText(helperA,"helper-a"); File.WriteAllText(helperA+".config","config-a");
            File.WriteAllText(helperB,"helper-b"); File.WriteAllText(helperB+".config","config-b");
            var updateEnv=new Dictionary<string,string>();
            var updateActivation=new NativeActivation(Path.Combine(args[0],"helper-update.json"),
                key=>updateEnv.ContainsKey(key)?updateEnv[key]:null,
                (key,value)=>{if(value==null)updateEnv.Remove(key);else updateEnv[key]=value;},()=>{});
            var deployedA=NativeActivation.InstallHelper(helperA); var deployedB=NativeActivation.InstallHelper(helperB);
            var updateManifest=Path.Combine(args[0],"update-manifest.json");
            File.WriteAllText(updateManifest,JsonData.Serializer().Serialize(new NativeBridgeSettings { RealCodex="manual-fixture", AutomaticRuntime=false }));
            var manifestBytes=File.ReadAllBytes(updateManifest);
            updateActivation.Enable(deployedA,updateManifest,()=>System.Threading.Tasks.Task.FromResult(0)).GetAwaiter().GetResult();
            var oldRecord=File.ReadAllText(Path.Combine(args[0],"helper-update.json"));
            Check(updateActivation.HelperStatus=="outdated","helper content mismatch is detected");
            updateActivation.UpdateHelper(helperB);
            Check(updateEnv["CODEX_CLI_PATH"]==deployedB&&File.Exists(deployedA)&&File.Exists(deployedB),"helper update switches registration and retains old bundle");
            Check(File.ReadAllText(Path.Combine(args[0],"helper-update.json"))!=oldRecord&&updateActivation.HelperStatus=="outdated","helper update is content addressed and repeatable");
            updateActivation.UpdateHelper(helperB);
            Check(updateEnv["CODEX_CLI_PATH"]==deployedB,"repeated helper update is idempotent");
            updateActivation.Disable();
            Check(updateEnv.Count==0,"helper update preserves disable recovery");
            Check(File.ReadAllBytes(updateManifest).SequenceEqual(manifestBytes),"helper update never rewrites manual runtime manifest");
            HelperUpdateChecks(args[0], helperA, helperB, updateManifest);
            activation.Enable(installed,"fixture-manifest",()=>System.Threading.Tasks.Task.FromResult(0)).GetAwaiter().GetResult();
            environment["CODEX_CLI_PATH"]="external";
            bool conflict=false;try{activation.Disable();}catch(InvalidOperationException){conflict=true;}
            Check(conflict&&environment["CODEX_CLI_PATH"]=="external","disable preserves external changes");
            environment["CODEX_CLI_PATH"]=installed;activation.Disable();
            Check(environment.Count==0&&!activation.Enabled&&File.Exists(installed),"disable restores original environment and retains helper");
            environment["CODEX_CLI_PATH"]="other-tool";
            conflict=false;try{activation.Enable(installed,"fixture",()=>System.Threading.Tasks.Task.FromResult(0)).GetAwaiter().GetResult();}catch(InvalidOperationException){conflict=true;}
            Check(conflict&&environment.Count==1,"third party CLI override blocks enable");environment.Clear();
            conflict=false;try{activation.Enable(installed,"fixture",()=>{throw new IOException("mock prepare failure");}).GetAwaiter().GetResult();}catch(IOException){conflict=true;}
            Check(conflict&&environment.Count==0&&!activation.Enabled,"prepare failure leaves environment unchanged");
            bool failWrite=true;
            var failing=new NativeActivation(Path.Combine(args[0],"failure.json"),key=>environment.ContainsKey(key)?environment[key]:null,
                (key,value)=>{if(key=="OPENCODEX_LAUNCHER_BRIDGE"&&failWrite){failWrite=false;throw new IOException("mock registry failure");}if(value==null)environment.Remove(key);else environment[key]=value;},()=>{});
            conflict=false;try{failing.Enable(installed,"fixture",()=>System.Threading.Tasks.Task.FromResult(0)).GetAwaiter().GetResult();}catch(IOException){conflict=true;}
            Check(conflict&&environment.Count==0&&!failing.Enabled,"partial environment failure restores both values");
            var record = new NativeProviderRecord { Provider = new ProviderOption { Id="mock", DisplayName="Mock", BaseUrl="http://127.0.0.1:1/v1", Adapter="openai-responses", RouteMode=ProviderRouteMode.NativeCodex }, Models=new[]{"test-model"} };
            NativeProviders.Save(record,"dummy-token"); NativeProviders.Save(record,null);
            Check(CredentialStore.Load(NativeProviders.CredentialId("mock"))=="dummy-token", "DPAPI key preserved on metadata save");
            using(var p=Process.Start(new ProcessStartInfo(exe,"--credential-isolated " + NativeControlBridge.Quote(args[0]) + " launcher_native_mock") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true })) {
                var output=p.StandardOutput.ReadToEnd(); var error=p.StandardError.ReadToEnd(); p.WaitForExit();
                Check(p.ExitCode==0 && output=="dummy-token" && error=="", "GUI helper writes token only");
            }
            Check(new ProviderOption().RouteMode==ProviderRouteMode.OpenCodexProxy,"legacy providers default to proxy");
            var block=NativeProviders.Block(new[]{record},exe);
            Check(!block.Contains("dummy-token") && block.Contains(".auth]"),"TOML has command binding without key");
            var patched=NativeProviders.Patch("model = 'gpt-5.5'\n",null,block);
            Check(NativeProviders.Patch(patched,block,block)==patched,"owned patch idempotent");
            bool rejected=false; try { NativeProviders.Patch(patched+"# external\n",block,block); } catch(IOException) { rejected=true; }
            Check(rejected,"external TOML edits preserved");
            var alias=NativeProviders.Alias("mock","test-model");
            Check(alias!=NativeProviders.Alias("other","test-model"),"same models have distinct provider aliases");
            var routes=new Dictionary<string,NativeRoute>{{alias,new NativeRoute{Provider="launcher_native_mock",Model="test-model",Mode=ProviderRouteMode.NativeCodex}}};
            var bridge=new NativeControlBridge(()=>routes); string errorText;
            var input=JsonData.Serializer().Serialize(new {id=1,method="thread/start",@params=new {model=alias,cwd="fixture",approvalPolicy="never"}});
            var result=bridge.Input(input,out errorText);
            var pms=JsonData.Object(JsonData.Value(JsonData.Parse(result),"params"));
            Check(errorText==null && JsonData.Text(pms,"modelProvider")=="launcher_native_mock" && JsonData.Text(pms,"model")=="test-model" && JsonData.Text(pms,"cwd")=="fixture","start rewrites model/provider and preserves parameters");
            bridge.Output("{\"id\":1,\"result\":{\"thread\":{\"id\":\"t1\"},\"modelProvider\":\"launcher_native_mock\"}}");
            input=JsonData.Serializer().Serialize(new {id=2,method="turn/start",@params=new {threadId="t1",model=alias}});
            Check(bridge.Input(input,out errorText)!=null && errorText==null,"same provider turn alias resolves");
            input=JsonData.Serializer().Serialize(new{id=21,method="turn/start",@params=new{threadId="t1",collaborationMode=new{mode="default",settings=new{model=alias,reasoning_effort="low",developer_instructions="fixture"}}}});
            result=bridge.Input(input,out errorText);
            Check(errorText==null&&!result.Contains(alias)&&result.Contains("test-model")&&result.Contains("fixture"),"Desktop collaboration model alias resolves without changing other settings");
            input=input.Replace("t1","unbound");
            Check(bridge.Input(input,out errorText)==null&&errorText.Contains("new conversation"),"nested alias on unbound thread fails closed");
            input=JsonData.Serializer().Serialize(new {id=3,method="thread/settings/update",@params=new {threadId="other",model=alias}});
            Check(bridge.Input(input,out errorText)==null && errorText.Contains("new conversation"),"cross provider switch fails closed");
            input="{\"id\":4,\"method\":\"thread/start\",\"params\":{\"model\":\"vendor/native\"}}";
            Check(bridge.Input(input,out errorText)==input,"native slash frame unchanged");
            input=JsonData.Serializer().Serialize(new {id=5,method="turn/start",@params=new {threadId="t1",model="proxy/some-model"}});
            Check(bridge.Input(input,out errorText)==null,"native thread cannot silently send proxy model to native upstream");
            var home=Path.Combine(args[0],"codex"); Directory.CreateDirectory(home);
            var catalog=Path.Combine(home,"catalog.json");
            File.WriteAllText(catalog,"{\"models\":[{\"slug\":\"gpt-5.5\",\"display_name\":\"Official\",\"visibility\":\"list\"}]}");
            var config=Path.Combine(home,"config.toml");
            File.WriteAllText(config,"model_catalog_json = '"+catalog+"'\nopenai_base_url = 'http://127.0.0.1:10100/v1'\n");
            var paths=new PathSet{Codex=Bundle(args[0],"prepare-runtime"),CodexHome=home,CodexConfig=config};
            NativeProviders.Prepare(paths,exe,false).GetAwaiter().GetResult();
            var first=File.ReadAllText(config);
            NativeProviders.Prepare(paths,exe,false).GetAwaiter().GetResult();
            Check(File.ReadAllText(config)==first && first.Contains("openai_base_url = 'http://127.0.0.1:10100/v1'"),"repeated prepare preserves proxy route");
            Check(File.ReadAllText(catalog).Contains(alias)&&File.Exists(NativeProviders.BridgePath),"prepare writes native catalog aliases and executable manifest");
            Check(!first.Contains("dummy-token"),"prepared TOML never contains credential");
            rejected=false;try{NativeProviders.Prepare(paths,exe,true).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&File.ReadAllText(config)==first,"Reserve Force blocks prepare without mutation");
            paths.OcxConfig=Path.Combine(args[0],"proxy.json"); paths.Catalog=catalog;
            var store=new ConfigStore();
            var proxy=new ProviderOption{Id="provider-a",DisplayName="Provider A",BaseUrl="https://example.invalid/v1",Adapter="openai-responses"};
            var other=new ProviderOption{Id="provider-b",DisplayName="Provider B",BaseUrl="https://example.invalid/v1",Adapter="openai-responses"};
            store.UpsertProviderAsync(paths.OcxConfig,proxy,"test-key").GetAwaiter().GetResult();
            store.UpsertProviderAsync(paths.OcxConfig,other,"dummy-token").GetAwaiter().GetResult();
            store.SelectProviderModelsAsync(paths.OcxConfig,proxy.Id,proxy.DisplayName,new[]{"same"},new[]{"same","unselected"}).GetAwaiter().GetResult();
            store.SelectProviderModelsAsync(paths.OcxConfig,other.Id,other.DisplayName,new[]{"same"},new[]{"same"}).GetAwaiter().GetResult();
            proxy.RouteMode=ProviderRouteMode.NativeCodex;
            ProviderManagement.Save(paths,proxy,null,new[]{"same"},new[]{"same","unselected"},false,exe).GetAwaiter().GetResult();
            Check(!store.Providers(paths.OcxConfig).Any(x=>x.Id==proxy.Id)&&NativeProviders.Read().Single(x=>x.Provider.Id==proxy.Id).DiscoveredModels.Length==2,"checkbox conversion removes relay registration and retains fetched models");
            Check(ProviderManagement.Key(paths.OcxConfig,proxy)=="test-key"&&store.SelectedModels(paths.OcxConfig,other.Id).SetEquals(new[]{"same"}),"conversion reuses DPAPI key and preserves other proxy selection");
            Check(File.ReadAllText(catalog).Contains(NativeProviders.Alias(proxy.Id,"same"))&&!File.ReadAllText(config).Contains("test-key"),"prepared native conversion updates alias and never writes key to TOML");
            var nativeBytes=CredentialStore.Snapshot(NativeProviders.CredentialId(proxy.Id));
            ProviderManagement.Save(paths,proxy,null,new[]{"same"},new[]{"same","unselected"},false,exe).GetAwaiter().GetResult();
            Check(nativeBytes.SequenceEqual(CredentialStore.Snapshot(NativeProviders.CredentialId(proxy.Id))),"native unchanged key preserves encrypted bytes");
            var managedConfig=File.ReadAllText(config);var metadata=File.ReadAllText(NativeProviders.StorePath);
            File.AppendAllText(config,"# external edit\n");
            rejected=false;try{ProviderManagement.Delete(paths,proxy.Id,false,exe).GetAwaiter().GetResult();}catch(IOException){rejected=true;}
            Check(rejected&&File.ReadAllText(NativeProviders.StorePath)==metadata&&ProviderManagement.Key(paths.OcxConfig,proxy)=="test-key"&&File.ReadAllText(config).EndsWith("# external edit\n"),"failed native deletion restores metadata and credential without overwriting external TOML");
            File.WriteAllText(config,managedConfig);
            proxy.RouteMode=ProviderRouteMode.OpenCodexProxy;
            ProviderManagement.Save(paths,proxy,null,new[]{"same"},new[]{"same","unselected"},false,exe).GetAwaiter().GetResult();
            Check(store.SelectedModels(paths.OcxConfig,proxy.Id).SetEquals(new[]{"same"})&&CredentialStore.ForProvider(paths.OcxConfig,proxy.Id)=="test-key"&&!File.ReadAllText(catalog).Contains(NativeProviders.Alias(proxy.Id,"same")),"unchecking native restores proxy selection and key and removes native alias");
            ProviderManagement.Delete(paths,proxy.Id,false,exe).GetAwaiter().GetResult();
            Check(!ProviderManagement.List(paths.OcxConfig).Any(x=>x.Id==proxy.Id)&&!CredentialStore.Exists(proxy.Id)&&store.SelectedModels(paths.OcxConfig,other.Id).Count==1,"proxy deletion removes provider and effective key but keeps other provider");
            var beforeDelete=File.ReadAllText(paths.OcxConfig);var root=JsonData.Parse(beforeDelete);root["defaultProvider"]=other.Id;File.WriteAllText(paths.OcxConfig,JsonData.Serializer().Serialize(root));
            rejected=false;try{ProviderManagement.Delete(paths,other.Id,false,exe).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&store.Providers(paths.OcxConfig).Any(x=>x.Id==other.Id)&&CredentialStore.Exists(other.Id),"default provider deletion is blocked without fallback or credential loss");
            ProviderManagement.Delete(paths,"mock",false,exe).GetAwaiter().GetResult();
            Check(NativeProviders.Read().Count==0&&!File.ReadAllText(catalog).Contains(alias)&&!File.ReadAllText(config).Contains("model_providers.'launcher_native_mock'")&&!CredentialStore.Exists("launcher_native_mock"),"last native provider deletion removes owned TOML aliases routes and effective credential");
            var emptyBridge=new NativeControlBridge(()=>JsonData.Serializer().Deserialize<Dictionary<string,NativeRoute>>(File.ReadAllText(NativeProviders.RoutesPath)));
            Check(emptyBridge.Input(JsonData.Serializer().Serialize(new{id=10,method="thread/start",@params=new{model=alias}}),out errorText)==null,"deleted cached alias fails closed instead of falling back");
            Console.WriteLine("ALL " + count + " NATIVE CHECKS PASSED"); return 0;
        } catch(Exception) { Console.Error.WriteLine("Native check failed; payloads suppressed."); return 1; }
    }
}
