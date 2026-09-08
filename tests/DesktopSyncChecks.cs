using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCodexLauncherV2;

static class DesktopSyncChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var home = Path.Combine(root, "sync-target"); Directory.CreateDirectory(home);
        var defaultHome = Path.Combine(root, "sync-default"); Directory.CreateDirectory(defaultHome);
        var paths = new PathSet { CodexHome=home, CodexConfig=Path.Combine(home,"config.toml"), Catalog=Path.Combine(home,"opencodex-catalog.json"), OcxConfig=Path.Combine(home,"config.json") };
        const string providers = "{\"port\":14567,\"providers\":{\"demo-provider\":{\"selectedModels\":[\"one\",\"two\"]}}}";
        const string route = "openai_base_url='http://127.0.0.1:14567/v1'\n";
        File.WriteAllText(paths.OcxConfig,providers); File.WriteAllText(paths.CodexConfig,route);
        var settings = new LauncherSettings { SetupCompleted=true, ConfigurationMode="manual" };
        var result = DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal);
        check(result.Code=="catalog-reference-missing"&&result.ExpectedModels==2,"tester report is incomplete even though two models are saved and proxy route exists");
        var calls=0;
        result=await DesktopSync.RunAsync(paths,t=>{calls++;return Task.FromResult(new CommandResult{Output="catalog sync skipped: no Codex catalog source found; keeping native catalog"});},CancellationToken.None);
        check(!result.Succeeded&&result.Upstream=="no-catalog-source"&&calls==1,"zero exit with missing source cannot report sync success or retry indefinitely");
        check(DesktopSync.Classify("catalog sync skipped: dummy-private-secret at private-endpoint")=="catalog-skipped","upstream failure is classified without retaining raw private output");
        File.WriteAllText(paths.CodexConfig,route+"model_catalog_json='"+paths.Catalog+"'\n");
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Code=="catalog-missing","reference to missing file is not a successful sync");
        File.WriteAllText(paths.Catalog,"{broken");
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Code=="catalog-invalid"&&File.ReadAllText(paths.Catalog)=="{broken","malformed catalog remains untouched after read-back failure");
        File.WriteAllText(paths.Catalog,"{\"models\":[{\"slug\":\"other-provider/one\"},{\"slug\":\"demo-provider/two\"}]}");
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Code=="models-missing","another provider's equal model suffix cannot satisfy sync verification");
        File.WriteAllText(paths.Catalog,"{\"models\":[{\"slug\":\"demo-provider/one\"},{\"slug\":\"demo-provider/two\",\"visibility\":\"hide\"}]}");
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Code=="models-missing","hidden selected model cannot pass visible catalog verification");
        const string completeCatalog="{\"models\":[{\"slug\":\"demo-provider/one\"},{\"slug\":\"demo-provider/two\"}]}";
        File.WriteAllText(paths.Catalog,completeCatalog);
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Succeeded,"complete developer-machine disk state verifies without Desktop process evidence or explicit association");
        var validConfig=File.ReadAllText(paths.CodexConfig);
        File.WriteAllText(paths.CodexConfig,route+"[mcp_servers.fixture.env]\nmodel_catalog_json='"+paths.Catalog+"'");
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Code=="catalog-reference-missing","nested catalog assignment does not repair root configuration");
        File.WriteAllText(paths.CodexConfig,route+"model_catalog_json='relative.json'");
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Code=="catalog-reference-relative","relative catalog references stay unverified instead of being guessed");
        File.WriteAllText(paths.CodexConfig,validConfig+"model_catalog_json='duplicate'");
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Code=="config-unsupported","duplicate root catalog keys cannot pass verification");
        File.WriteAllText(paths.CodexConfig,validConfig.Replace("14567","14569"));
        check(DesktopSync.Verify(paths,DesktopDiagnosticInputs.ReadLocal).Code=="route-port-mismatch","wrong proxy port prevents sync success");
        File.WriteAllText(paths.CodexConfig,validConfig);
        result=await DesktopSync.RunAsync(paths,t=>Task.FromResult(new CommandResult{ExitCode=1}),CancellationToken.None);
        check(!result.Succeeded&&result.Code=="command-failed","nonzero upstream exit cannot be hidden by an old valid catalog");
        result=await DesktopSync.RunAsync(paths,t=>{File.WriteAllText(paths.OcxConfig,providers.Replace("two","three"));return Task.FromResult(new CommandResult());},CancellationToken.None);
        check(result.Code=="selection-changed","concurrent model selection changes stop stale success");
        File.WriteAllText(paths.OcxConfig,providers);
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool stopped=false;try {await DesktopSync.RunAsync(paths,t=>{calls++;return Task.FromResult(new CommandResult());},cancel.Token);}catch(OperationCanceledException){stopped=true;}
            check(stopped&&calls==1,"cancelled sync does not invoke the CLI");
        }
        File.WriteAllText(paths.OcxConfig,"{broken");bool rejected=false;
        try {await DesktopSync.RunAsync(paths,t=>{calls++;return Task.FromResult(new CommandResult());},CancellationToken.None);}catch {rejected=true;}
        check(rejected&&calls==1&&File.ReadAllText(paths.OcxConfig)=="{broken","broken provider configuration prevents upstream writes");
        File.WriteAllText(paths.OcxConfig,providers);
        check(DesktopSync.Selected("{}").Count==0,"empty initialized provider object needs no fabricated models");
        rejected=false;try{DesktopSync.Selected("{\"providers\":{\"demo-provider\":{\"selectedModels\":\"one\"}}}");}catch{rejected=true;}
        check(rejected,"malformed non-array selection cannot masquerade as an empty list");
        result=await DesktopSync.RunAsync(paths,t=>{File.WriteAllText(paths.OcxConfig,"{broken");return Task.FromResult(new CommandResult());},CancellationToken.None);
        check(result.Code=="provider-invalid"&&File.ReadAllText(paths.OcxConfig)=="{broken","provider corruption during sync yields a reportable result and preserves bytes");
        File.WriteAllText(paths.OcxConfig,providers);
        var defaultConfig=Path.Combine(defaultHome,"config.toml"); File.WriteAllText(defaultConfig,"model='fixture'\n");
        int reads=0;var input=new DesktopDiagnosticInputs {DefaultHome=defaultHome,EnvironmentHome=defaultHome,Read=p=>{reads++;return DesktopDiagnosticInputs.ReadLocal(p);}};
        check(DesktopCandidates.Find(new LauncherSettings(),paths,input).Count==0&&reads==0,"candidate discovery before onboarding reads nothing");
        var candidates=DesktopCandidates.Find(settings,paths,input);
        check(candidates.Count==2&&candidates.Select(c=>c.ConfigPath).Distinct().Count()==2,"candidate discovery deduplicates environment and default homes and includes manual target");
        check(candidates[0].ConfigPath==defaultConfig&&String.IsNullOrEmpty(settings.DesktopConfigPath),"discovery offers external candidate without automatically saving an association");
        var candidate=candidates[0];var originalHash=FileTransaction.Hash(defaultConfig);
        var next=DesktopCandidates.Confirm(settings,candidate,input);
        check(next.DesktopConfigPath==defaultConfig&&String.IsNullOrEmpty(settings.DesktopConfigPath)&&FileTransaction.Hash(defaultConfig)==originalHash,"candidate confirmation prepares association without changing config or credentials");
        File.WriteAllText(defaultConfig,"# external change");rejected=false;
        try{DesktopCandidates.Confirm(settings,candidate,input);}catch(IOException){rejected=true;}
        check(rejected&&File.ReadAllText(defaultConfig)=="# external change","candidate changed during confirmation is rejected and preserved");
        input.EnvironmentHome=null;File.WriteAllText(defaultConfig,"# default fixture");settings.DesktopConfigPath=paths.CodexConfig;
        candidates=DesktopCandidates.Find(settings,paths,input);
        check(candidates[0].Role=="associated"&&candidates[0].ConfigPath==paths.CodexConfig,"existing explicit association remains first and is not switched to default silently");
        var catalogHash=FileTransaction.Hash(paths.Catalog);
        result=await DesktopSync.RunAsync(paths,t=>Task.FromResult(new CommandResult()),CancellationToken.None);
        check(result.Succeeded&&FileTransaction.Hash(paths.Catalog)==catalogHash,"successful read-back uses actual referenced catalog without generating fake model metadata");
    }
}
