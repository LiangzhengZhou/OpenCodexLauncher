using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Net;
using System.Net.Sockets;
using OpenCodexLauncherV2;

static class ReleaseChecks
{
    public static async Task Run(string root, Action<bool,string> check)
    {
        var fresh = PathResolver.Load();
        check(!fresh.SetupCompleted && fresh.SettingsVersion == 1 && !fresh.ReserveForceEnabled, "new settings require onboarding with Reserve Force off");
        check(PathResolver.Empty().OcxConfig == null && PathResolver.Empty().Catalog == null, "onboarding path set has no linked user files");
        check(!File.Exists(PathResolver.SettingsPath()), "loading new defaults does not save or import anything");
        var legacy = SetupService.Normalize(new LauncherSettings { OcxPath = "fixture", LaunchStrategy = "auto", StrictRouteVerification = false, ReserveForceEnabled = true }, true);
        check(legacy.SetupCompleted && legacy.LaunchStrategy == "auto" && legacy.ReserveForceEnabled && !legacy.StrictRouteVerification, "legacy migration preserves strategy and advanced settings");
        var manual = PathResolver.Resolve(new LauncherSettings { ConfigurationMode = "manual" });
        SetupService.Prepare(new LauncherSettings { ConfigurationMode = "manual" }, manual);
        check(!Directory.Exists(manual.CodexHome), "incomplete onboarding creates no configuration homes");
        check(manual.Ocx == null && manual.Codex == null && manual.OcxConfig.StartsWith(LocalEnvironment.Current.DataDirectory), "manual setup uses private empty homes without executable discovery");
        Environment.SetEnvironmentVariable("OPENCODEX_HOME", root);
        check(!PathResolver.Resolve(fresh).OcxConfig.Equals(Path.Combine(root,"config.json")), "isolated paths ignore real environment configuration");
        Environment.SetEnvironmentVariable("OPENCODEX_HOME", null);
        var info = new ProcessStartInfo { UseShellExecute = false }; AsyncProcessRunner.ApplyHomes(info, manual.OcxConfig, manual.CodexHome);
        check(info.EnvironmentVariables["CODEX_HOME"] == manual.CodexHome && info.EnvironmentVariables["OPENCODEX_HOME"] == Path.GetDirectoryName(manual.OcxConfig), "child processes receive selected configuration homes");
        check(Directory.Exists(manual.CodexHome) && Directory.Exists(Path.GetDirectoryName(manual.OcxConfig)), "launch repairs missing launcher-owned homes for existing installations");
        check(Directory.GetFiles(manual.CodexHome).Length == 1 && File.ReadAllText(manual.CodexConfig) == "" && !File.Exists(manual.OcxConfig), "home initialization creates only empty TOML and never imports account files");
        var sentinel = Path.Combine(manual.CodexHome, "config.toml"); File.WriteAllText(sentinel, "# retained fixture");
        SetupService.Prepare(new LauncherSettings { ConfigurationMode = "manual", SetupCompleted = true }, manual);
        check(File.ReadAllText(sentinel) == "# retained fixture", "repeated setup preserves existing configuration bytes");
        var missing = Path.Combine(root, "missing-import"); bool refused = false;
        try { AsyncProcessRunner.ApplyHomes(new ProcessStartInfo(), null, missing); } catch(IOException error) { refused = error.Message.Contains("CODEX_HOME"); }
        check(refused && !Directory.Exists(missing), "missing imported home is reported without inventing a replacement");
        var collision = Path.Combine(root, "home-is-file"); File.WriteAllText(collision, "retained"); refused = false;
        try { SetupService.PrepareHome(collision, "CODEX_HOME"); } catch(IOException) { refused = true; }
        check(refused && File.ReadAllText(collision) == "retained", "file at a home path is rejected without overwrite");
        var fixture = Process.GetCurrentProcess().MainModule.FileName;
        var command = new CommandSpec { File = fixture, Arguments = "startup-failure", Directory = root };
        string diagnostic = "";
        try { await new AsyncProcessRunner().CheckStartupAsync(command, manual.OcxConfig, manual.CodexHome, CancellationToken.None); }
        catch(InvalidOperationException error) { diagnostic = error.Message; }
        check(diagnostic.Contains("fixture import failed") && diagnostic.Contains("17"), "startup preflight reports child stderr and exit status");
        check(!diagnostic.Contains("dummy-startup-secret") && diagnostic.Contains("REDACTED"), "startup preflight redacts credential-shaped output");
        command.Arguments = "startup-home";
        await new AsyncProcessRunner().CheckStartupAsync(command, manual.OcxConfig, manual.CodexHome, CancellationToken.None);
        check(true, "startup preflight succeeds with existing selected homes");
        using(var cancelled = new CancellationTokenSource()) {
            cancelled.Cancel(); refused = false;
            try { await new AsyncProcessRunner().CheckStartupAsync(command, manual.OcxConfig, manual.CodexHome, cancelled.Token); } catch(OperationCanceledException) { refused = true; }
            check(refused, "cancelled startup preflight does not start the child");
        }
        CredentialStore.Save("fixture-key", "dummy-test-only");
        check(CredentialStore.PathFor("fixture-key").StartsWith(LocalEnvironment.Current.DataDirectory) && CredentialStore.Load("fixture-key") == "dummy-test-only", "DPAPI credentials roundtrip inside isolated store");
        Directory.CreateDirectory(LocalEnvironment.Current.DataDirectory);
        File.WriteAllText(PathResolver.SettingsPath(), "{broken");
        var brokenHash = FileTransaction.Hash(PathResolver.SettingsPath()); bool rejected = false;
        try { PathResolver.Load(); } catch { rejected = true; }
        check(rejected && FileTransaction.Hash(PathResolver.SettingsPath()) == brokenHash, "corrupt settings are rejected without overwrite");
        PathResolver.Save(fresh);
        var language = L.M("text.004"); L.SetLanguage("en"); var before = language.Value; L.SetLanguage("zh");
        check(before == "Overview" && language.Value == "概览" && L.Complete, "all resource keys have both languages and live messages update");
        var composite = L.M("text.054") + "demo-provider/model"; L.SetLanguage("en");
        check(composite.Value == "Model: demo-provider/model", "language switch preserves literal model IDs in composite messages");
        var start = DateTime.UtcNow.AddMinutes(-1);
        var session = new RunningSession { RequestSessionId = "session-a", StartedAt = start, ModelRouteId = "demo-provider/shared" };
        var own = new Dictionary<string,object> { {"sessionId","session-a"}, {"timestamp",DateTime.UtcNow.AddSeconds(-2).ToString("o")}, {"provider","demo-provider"}, {"resolvedModel","shared"}, {"status",200} };
        var other = new Dictionary<string,object>(own); other["sessionId"]="session-b";other["provider"]="other";other["timestamp"]=DateTime.UtcNow.ToString("o");
        var rows = new Dictionary<string,object>{{"logs",new object[]{own,other}}};
        var observation=SessionEvidence.Find(rows,session);
        check(observation != null && SessionEvidence.Route(observation,session.ModelRouteId)==session.ModelRouteId, "concurrent sessions cannot replace current session evidence");
        session.RequestSessionId=null; check(SessionEvidence.Find(rows,session)==null,"unassociated requests remain pending"); session.RequestSessionId="session-a";
        own.Remove("timestamp"); check(SessionEvidence.Find(rows,session)==null,"missing timestamp cannot verify a session");
        own["timestamp"]=start.AddSeconds(-1).ToString("o");check(SessionEvidence.Find(rows,session)==null,"requests before launch cannot verify a session");
        own["timestamp"]=DateTime.UtcNow.AddDays(1).ToString("o");check(SessionEvidence.Find(rows,session)==null,"future timestamp cannot verify a session");
        own["timestamp"]=DateTime.UtcNow.ToString("o"); own["status"]=500; check(SessionEvidence.Find(rows,session)==null,"failed requests do not count as verified routes");
        own["status"]=200; own["provider"]="other";
        bool mismatch=false;try{RouteVerifier.AssertModelInvariant(session.ModelRouteId,SessionEvidence.Route(SessionEvidence.Find(rows,session),session.ModelRouteId));}catch(ModelInvariantViolation){mismatch=true;}
        check(mismatch,"same model name at another provider is a route mismatch");
        var budget=new FallbackBudget();check(!budget.TryUse(false) && budget.TryUse(true) && !budget.TryUse(true),"strict switch gates fallback and retry budget is one");
        var file=Path.Combine(root,"transaction.txt"); File.WriteAllText(file,"original");
        var transaction=new FileTransaction(new[]{file});
        try { await transaction.Step(()=>{TextFile.AtomicWrite(file,"partial patch", Encoding.UTF8);throw new IOException("injected failure");}); } catch(IOException){transaction.Rollback();}
        check(File.ReadAllText(file)=="original", "failed transaction restores original bytes");
        transaction=new FileTransaction(new[]{file}); await transaction.Step(()=>{TextFile.AtomicWrite(file,"owned change", Encoding.UTF8);return Task.FromResult(0);});File.WriteAllText(file,"external change");
        bool guarded=false;try{transaction.Rollback();}catch(IOException){guarded=true;}
        check(guarded && File.ReadAllText(file)=="external change", "rollback refuses to overwrite external edits");
        var absent=Path.Combine(root,"created-by-transaction.txt");transaction=new FileTransaction(new[]{absent});await transaction.Step(()=>{TextFile.AtomicWrite(absent,"new", Encoding.UTF8);return Task.FromResult(0);});transaction.Rollback();
        check(!File.Exists(absent),"rollback removes only files created by the transaction");
        var badConfig=Path.Combine(root,"broken-ocx.json");File.WriteAllText(badConfig,"{invalid");brokenHash=FileTransaction.Hash(badConfig);rejected=false;
        try{await new ConfigStore().SetCodexIntegrationAsync(badConfig,true);}catch{rejected=true;}
        check(rejected && FileTransaction.Hash(badConfig)==brokenHash,"corrupt provider config is never replaced by a mutating operation");
        var package=Path.Combine(root,"unsupported");Directory.CreateDirectory(Path.Combine(package,"src"));Directory.CreateDirectory(Path.Combine(package,"bin"));
        var router=Path.Combine(package,"src","router.ts");File.WriteAllText(router,"incompatible source");var entry=Path.Combine(package,"bin","ocx.mjs");File.WriteAllText(entry,"");rejected=false;
        try{OpenCodexCompatibility.EnsureReservePreRouting(entry);}catch(InvalidOperationException){rejected=true;}
        check(rejected && File.ReadAllText(router)=="incompatible source","unsupported upstream is rejected before patching");
        transaction=new FileTransaction(new[]{file}); guarded=false;
        try { await transaction.Step(()=>{File.WriteAllText(file,"external during step");TextFile.AtomicWrite(file,"owned overwrite",Encoding.UTF8);return Task.FromResult(0);}); } catch(IOException){guarded=true;}
        check(guarded && File.ReadAllText(file)=="external during step","transaction refuses external edits during a multi-write step");
        await NetworkChecks(root, check);
    }

    sealed class MockServer : IDisposable
    {
        readonly TcpListener listener = new TcpListener(IPAddress.Loopback,0);
        readonly Task worker;
        public string Models = "{\"data\":[{\"id\":\"other/shared\"},{\"id\":\"demo-provider/shared\"}]}";
        public string Logs = "{}";
        public int Requests;
        public int Port { get; private set; }
        public MockServer()
        {
            listener.Start(); Port=((IPEndPoint)listener.LocalEndpoint).Port;
            worker=Task.Run(async ()=> {
                try { while(true) {
                    using(var peer=await listener.AcceptTcpClientAsync()) using(var stream=peer.GetStream()) using(var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true)) {
                        stream.ReadTimeout=3000; stream.WriteTimeout=3000;
                        var line=reader.ReadLine() ?? ""; while(!String.IsNullOrEmpty(reader.ReadLine())){}
                        Interlocked.Increment(ref Requests);
                        var body=line.Contains("/logs") ? Logs : line.Contains("/models") ? Models : "{}";
                        var bytes=Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\nConnection: close\r\n\r\n"+body);
                        stream.Write(bytes,0,bytes.Length);
                    }
                }} catch(ObjectDisposedException){} catch(SocketException){} catch(InvalidOperationException){}
            });
        }
        public void Dispose(){listener.Stop(); if(!worker.Wait(5000))throw new TimeoutException("Mock server did not stop");}
    }
    static async Task NetworkChecks(string root,Action<bool,string> check)
    {
        using(var server=new MockServer()) using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            var home=Path.Combine(root,"mock-network");Directory.CreateDirectory(home);
            var cfg=Path.Combine(home,"config.json");File.WriteAllText(cfg,"{\"port\":"+server.Port+"}");
            File.WriteAllText(Path.Combine(home,"runtime-port.json"),"{broken");
            check(OpenCodexEndpointResolver.Candidates(cfg).Single()==server.Port,"malformed runtime port does not hide configured port");
            var endpoint=await OpenCodexEndpointResolver.ResolveAsync(cfg,timeout.Token);
            check(endpoint.Port==server.Port,"dynamic endpoint resolves against mock health service");
            var client=new OpenCodexClient(cfg);var resolver=new ClaudeModelResolver(client);
            check(await resolver.ResolveAsync("demo-provider/shared",timeout.Token)=="demo-provider/shared","HTTP model selection uses complete provider route");
            server.Models="{\"data\":[{\"id\":\"other/shared\"},{\"id\":\"alias-shared\"}]}";
            bool rejected=false;try{await resolver.ResolveAsync("demo-provider/shared",timeout.Token);}catch(ModelUnavailableError){rejected=true;}
            check(rejected,"gateway suffix aliases cannot select another provider");
            var session=new RunningSession{StartedAt=DateTime.UtcNow.AddMinutes(-1),ModelRouteId="demo-provider/shared"};
            var count=server.Requests;check(await client.GetSessionObservationAsync(session,timeout.Token)==null && count==server.Requests,"pending session performs no global log request");
            session.RequestSessionId="conversation-a";
            server.Logs=JsonData.Serializer().Serialize(new {logs=new[]{new {conversationId="conversation-a",timestamp=DateTime.UtcNow.ToString("o"),provider="demo-provider",resolvedModel="shared",status=200},new {conversationId="conversation-b",timestamp=DateTime.UtcNow.ToString("o"),provider="other",resolvedModel="shared",status=200}}});
            check(await new RuntimeController(new PathSet(),client,timeout.Token).VerifySessionRouteAsync(session),"HTTP route verification ignores other concurrent conversations");
            using(var canceled=new CancellationTokenSource()) { canceled.Cancel();rejected=false;try{await OpenCodexEndpointResolver.ResolveAsync(cfg,canceled.Token);}catch(OperationCanceledException){rejected=true;}check(rejected,"endpoint resolution respects window lifetime cancellation"); }
        }
    }
}
