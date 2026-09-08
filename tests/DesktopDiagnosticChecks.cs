using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenCodexLauncherV2;

static class DesktopDiagnosticChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var home = Path.Combine(root, "diagnostic-target");
        var other = Path.Combine(root, "diagnostic-default");
        var paths = new PathSet { CodexHome=home, CodexConfig=Path.Combine(home,"config.toml"), Catalog=Path.Combine(home,"opencodex-catalog.json"), OcxConfig=Path.Combine(root,"diagnostic-provider","config.json") };
        var settings = new LauncherSettings { SetupCompleted=true, ConfigurationMode="manual" };
        var files = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        files[paths.OcxConfig] = "{\"port\":14567,\"providers\":{\"demo-provider\":{\"selectedModels\":[\"fixture\"],\"apiKey\":\"dummy-diagnostic-secret\",\"baseUrl\":\"https://example.invalid\"}}}";
        files[paths.Catalog] = "{\"models\":[{\"slug\":\"demo-provider/fixture\",\"visibility\":\"list\"}]}";
        files[paths.CodexConfig] = "model_catalog_json = '" + paths.Catalog + "'\nopenai_base_url = 'http://127.0.0.1:14567/v1'\n";
        files[Path.Combine(other,"config.toml")] = "model='demo-provider/fixture'\n[mcp_servers.fixture.env]\nCODEX_HOME='dummy-private-home'\nmodel_catalog_json='must-not-read'\n";
        var reads = new List<string>(); var probes = new List<int>(); int processReads = 0;
        var input = new DesktopDiagnosticInputs {
            DefaultHome=other, Read=p=> { reads.Add(p); string value; return files.TryGetValue(p,out value)?value:null; },
            Processes=t=> { processReads++; return Task.FromResult(new DesktopProcessEvidence {Available=true,DesktopCandidates=1,AppServers=1}); },
            Health=(p,t)=> { probes.Add(p); return Task.FromResult(p==14567); }
        };
        var fresh = await DesktopDiagnostics.CollectAsync(paths,new LauncherSettings(),input,CancellationToken.None);
        check(fresh.Findings.Contains("setup-required")&&reads.Count==0&&probes.Count==0&&processReads==0,"desktop diagnostics before onboarding perform zero file, process and network access");
        var report = await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(report.Findings.Contains("possible-home-mismatch"),"desktop diagnostics reproduce populated manual home versus default home without root catalog reference");
        check(report.Findings.Contains("app-server-active")&&!report.Findings.Contains("target-models-missing"),"running app-server is reported without misclassifying successful catalog writing");
        check(report.Json.Contains("candidates-only")&&report.Json.Contains("unverified"),"candidate directory and process observations never claim Desktop loaded the catalog");
        check(probes.SequenceEqual(new[]{14567}),"diagnostics probe the configured dynamic port");
        files[Path.Combine(Path.GetDirectoryName(paths.OcxConfig),"runtime-port.json")]="{\"port\":14568}";
        input.Health=(p,t)=>Task.FromResult(p==14568);
        var dynamicReport=await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(dynamicReport.Findings.Contains("target-route-differs")&&dynamicReport.Json.Contains("\"routeMatchesHealthyProxy\":false"),"diagnostics detect a target pointing at an old port while the runtime port is healthy");
        files.Remove(Path.Combine(Path.GetDirectoryName(paths.OcxConfig),"runtime-port.json"));input.Health=(p,t)=>Task.FromResult(p==14567);
        check(!reads.Contains("must-not-read")&&!reads.Any(p=>p.EndsWith("auth.json")),"diagnostics ignore nested MCP fields and never read account credentials");
        check(!report.Json.Contains("demo-provider")&&!report.Json.Contains("fixture")&&!report.Json.Contains("dummy-")&&!report.Json.Contains("example.invalid")&&!report.Json.Contains(root.Replace("\\","\\\\")),"report allowlist excludes provider/model names, paths, endpoints and credentials");
        var original = files[paths.CodexConfig];
        files[paths.Catalog] = "{\"models\":[{\"slug\":\"other-provider/fixture\",\"visibility\":\"list\"}]}";
        report = await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(report.Findings.Contains("target-models-missing")&&report.Json.Contains("\"selectedModelsMissing\":1"),"same model suffix from another provider cannot satisfy the selected route");
        check(files[paths.CodexConfig]==original,"diagnostics leave original configuration bytes unchanged");
        files[paths.Catalog] = "{broken";
        report = await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(report.Findings.Contains("target-catalog-unreadable")&&files[paths.Catalog]=="{broken","corrupt catalog stays intact and is reported as unreadable");
        files[paths.CodexConfig] = "# empty root\n[mcp_servers.fixture.env]\nmodel_catalog_json='ignored'\nopenai_base_url='ignored'";
        report = await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(report.Findings.Contains("target-catalog-reference-missing")&&report.Findings.Contains("target-route-missing"),"nested MCP settings never substitute for missing root routing keys");
        bool supported;
        var keys=DesktopDiagnostics.RootKeys("model_catalog_json = \"catalog.json\" # comment\nopenai_base_url='http://127.0.0.1:14567/v1'",out supported);
        check(supported&&keys.Count==2&&keys["model_catalog_json"]=="catalog.json","diagnostic root reader supports basic and literal strings with comments");
        DesktopDiagnostics.RootKeys("note='''\nmodel_catalog_json='not-root'\n'''",out supported);
        check(!supported,"multiline TOML stays unknown instead of generating a false root reference");
        DesktopDiagnostics.RootKeys("model='first'\nmodel='second'",out supported);
        check(!supported,"duplicate diagnostic root keys are unsupported rather than guessed");
        files[paths.CodexConfig] = "model_catalog_json='relative.json'";
        report = await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(report.Findings.Contains("relative-catalog")&&!reads.Contains("relative.json"),"relative catalog references are not resolved using an invented base directory");
        input.Processes=t=>Task.FromResult(new DesktopProcessEvidence()); input.Health=(p,t)=>Task.FromResult(false);
        report = await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(report.Findings.Contains("processes-unknown")&&!report.Findings.Contains("desktop-not-detected")&&report.Findings.Contains("proxy-unavailable"),"process access failure means unknown, while failed health checks remain separate");
        input.EnvironmentHome=home;input.DefaultHome=home;reads.Clear();
        report = await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(reads.Count(p=>p==paths.CodexConfig)==1,"candidate homes equal to target do not reread configuration");
        input.Read=p=> {throw new IOException("dummy-private-error");};
        report = await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(report.Findings.Contains("provider-unreadable")&&!report.Json.Contains("dummy-private-error"),"file access errors produce fixed states without raw exception messages");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();bool stopped=false;try{await DesktopDiagnostics.CollectAsync(paths,settings,input,cancelled.Token);}catch(OperationCanceledException){stopped=true;}
            check(stopped,"cancelled diagnostics stop without continuing into probes");
        }
        input.Read=p=>null;var entered=new TaskCompletionSource<bool>();
        input.Health=async(p,t)=> { entered.TrySetResult(true); await Task.Delay(30000,t);return true; };
        using(var cancelled=new CancellationTokenSource())
        {
            var pending=DesktopDiagnostics.CollectAsync(paths,settings,input,cancelled.Token);await entered.Task;cancelled.Cancel();
            bool stopped=false;try{await pending;}catch(OperationCanceledException){stopped=true;}
            check(stopped,"in-flight health inspection is cancelled when the window lifetime ends");
        }
        var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        using(var listener=new HttpListener())
        {
            listener.Prefixes.Add("http://127.0.0.1:"+port+"/");listener.Start();
            var request=listener.GetContextAsync();var pending=DesktopDiagnosticInputs.ProbeHealth(port,CancellationToken.None);
            var ctx=await request;var path=ctx.Request.RawUrl;var auth=ctx.Request.Headers["Authorization"];
            ctx.Response.StatusCode=302;ctx.Response.RedirectLocation="https://example.invalid/never-follow";ctx.Response.Close();
            check(!await pending&&path=="/healthz"&&auth==null,"real loopback probe sends only health GET without auth and refuses redirects");
            request=listener.GetContextAsync();pending=DesktopDiagnosticInputs.ProbeHealth(port,CancellationToken.None);
            ctx=await request;ctx.Response.StatusCode=200;ctx.Response.ContentLength64=0;ctx.Response.Close();
            check(await pending,"real loopback health response succeeds without consuming service logs");
            listener.Stop();
        }
        var large=Path.Combine(root,"diagnostic-too-large.txt");File.WriteAllBytes(large,new byte[2*1024*1024+1]);
        bool bounded=false;try{DesktopDiagnosticInputs.ReadLocal(large);}catch(IOException){bounded=true;}
        check(bounded,"diagnostic filesystem reader rejects oversized input");
        check(DesktopDiagnosticInputs.ReadLocal(Path.Combine(root,"diagnostic-missing.toml"))==null,"missing local files are reported without creating them");
        bool refused=false;try{DesktopDiagnosticInputs.ReadLocal(Path.Combine(root,"auth.json"));}catch(IOException){refused=true;}
        check(refused,"catalog references cannot cause the local reader to open auth.json");
        input.Health=(p,t)=>new TaskCompletionSource<bool>().Task;
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var timed=await DesktopDiagnostics.CollectAsync(paths,settings,input,CancellationToken.None);
        check(timed.Findings.Contains("timed-out")&&clock.Elapsed.TotalSeconds<18,"overall deadline returns a safe report even if an injected probe ignores cancellation");
    }
}
