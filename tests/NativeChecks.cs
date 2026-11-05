using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using OpenCodexLauncherV2;
class NativeChecks
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); count++; }
    static string Bundle(string root, string name, string core)
    {
        var directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "codex.exe"), core);
        File.WriteAllText(Path.Combine(directory, "codex-code-mode-host.exe"), "host");
        return Path.Combine(directory, "codex.exe");
    }
    static string Package(string root, string name, string core)
    {
        var directory = Path.Combine(root, name); var resources = Path.Combine(directory, "app", "resources");
        Directory.CreateDirectory(resources);
        File.WriteAllText(Path.Combine(resources, "codex.exe"), core);
        return directory;
    }
    static string PackageBundle(string root, string name, string core)
    {
        var directory = Package(root, name, core);
        var resources = Path.Combine(directory, "app", "resources");
        File.WriteAllText(Path.Combine(resources, "codex-code-mode-host.exe"), "host");
        return directory;
    }
    static void RuntimeChecks(string directory)
    {
        var root = Path.Combine(directory, "runtime-fixtures");
        var package = Package(directory, "desktop-package-b", "package-core-B");
        var a = Bundle(root, "A", "core-A");
        var b = Bundle(root, "B", "package-core-B");
        var manifest = new NativeBridgeSettings { RealCodex = a, CodexHome = "fixture-home", RoutesPath = "fixture-routes", AutomaticRuntime = false };
        var manifestPath = Path.Combine(directory, "rollover-manifest.json");
        Action persist = () => File.WriteAllText(manifestPath, JsonData.Serializer().Serialize(manifest));
        persist();
        var activation = new NativeActivation(Path.Combine(directory, "rollover-activation.json"), key => key == "CODEX_CLI_PATH" ? b : manifestPath, (key, value) => { throw new Exception(); }, () => { });
        File.WriteAllText(Path.Combine(directory, "rollover-activation.json"), JsonData.Serializer().Serialize(new NativeActivationState { Helper = b, Manifest = manifestPath }));
        Check(activation.Enabled && activation.RuntimeHealthFor(package, root) == "healthy", "activation registration and explicit runtime health are independent");
        int scans = 0; Func<string,string> discover = r => { scans++; return CodexRuntime.FindUsable(r); };
        Check(CodexRuntime.ResolveNative(manifest, root, discover, package) == a && scans == 0, "explicit complete runtime is respected without discovery");

        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddHours(1));
        manifest.AutomaticRuntime = true; persist();
        Check(CodexRuntime.ResolveNative(manifest, root, discover, package) == b, "automatic mode selects the package-compatible runtime even when old A is complete and newer by timestamp");
        Check(activation.RuntimeHealthFor(package, root) == "outdated", "helper health reports a complete but outdated runtime");
        manifest.RealCodex = b; persist();
        Check(activation.RuntimeHealthFor(package, root) == "healthy", "helper health becomes healthy when the bridge matches the package");

        var packageBundle = PackageBundle(directory, "desktop-package-e", "package-core-E");
        var bundleCore = Path.Combine(packageBundle, "app", "resources", "codex.exe");
        manifest.RealCodex = a; manifest.AutomaticRuntime = true; persist();
        Check(CodexRuntime.ResolveNative(manifest, root, discover, packageBundle) == bundleCore, "automatic mode falls back to a complete package core when no local runtime matches");
        Check(CodexRuntime.FindCurrentDesktopRuntime(packageBundle, root) == bundleCore, "GUI discovery uses the complete package core fallback");
        Check(activation.RuntimeHealthFor(packageBundle, root) == "outdated", "package-core fallback is reported as a runtime change");
        manifest.RealCodex = bundleCore; persist();
        Check(activation.RuntimeHealthFor(packageBundle, root) == "healthy", "package-core fallback becomes healthy after Prepare records it");
        manifest.RealCodex = a; persist();
        File.Delete(Path.Combine(Path.GetDirectoryName(a), "codex-code-mode-host.exe"));
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddHours(1));
        Check(File.Exists(a) && !CodexRuntime.IsUsable(a) && CodexRuntime.IsUsable(b), "rollover leaves executable but removes required host");
        Check(CodexRuntime.FindUsable(root) == b, "newest incomplete executable excluded before ordering");
        Check(CodexRuntime.ResolveNative(manifest, root, discover, package) == b, "automatic resolution replaces incomplete A with package-compatible B");
        Check(activation.RuntimeHealthFor(package, root) == "outdated", "incomplete old runtime is reported as recoverable/outdated");
        var persisted = File.ReadAllText(manifestPath);
        Check(CodexRuntime.ResolveNative(JsonData.Serializer().Deserialize<NativeBridgeSettings>(persisted), root, discover, package) == b && File.ReadAllText(manifestPath) == persisted, "persisted stale manifest resolves without disk mutation");
        Check(JsonData.Serializer().Deserialize<NativeBridgeSettings>("{\"RealCodex\":\"fixture\",\"CodexHome\":\"home\",\"RoutesPath\":\"routes\"}").AutomaticRuntime == null, "3.1.0 manifest without provenance remains readable");
        Check(manifest.RealCodex == a && manifest.CodexHome == "fixture-home" && manifest.RoutesPath == "fixture-routes", "resolution leaves manifest and routing configuration untouched");

        var legacyInside = new NativeBridgeSettings { RealCodex = a, CodexHome = "fixture-home", RoutesPath = "fixture-routes", AutomaticRuntime = null };
        Check(CodexRuntime.ResolveNative(legacyInside, root, discover, package) == b, "legacy manifest inside Desktop root upgrades to the package-compatible runtime");
        Check(activation.RuntimeHealthFor(package, root) == "outdated", "legacy manifest health reports outdated");

        var packageNoMatch = Package(directory, "desktop-package-d", "package-core-D");
        manifest.RealCodex = b; manifest.AutomaticRuntime = true; persist();
        bool rejected = false;
        try { CodexRuntime.ResolveNative(manifest, root, discover, packageNoMatch); }
        catch (NativeRuntimeException error) { rejected = error.Code == "no-compatible-runtime"; }
        Check(rejected, "automatic mode fails closed when no runtime matches the installed package");
        Check(activation.RuntimeHealthFor(packageNoMatch, root) == "incompatible", "no package-compatible runtime is reported as incompatible");

        manifest.RealCodex = a; persist();
        File.Delete(a);
        Check(activation.RuntimeHealthFor(package, root) == "outdated", "missing recorded runtime is recoverable to the package runtime");
        Check(CodexRuntime.ResolveNative(manifest, root, discover, package) == b, "missing executable resolves to the package-compatible runtime");

        manifest.AutomaticRuntime = false; persist();
        rejected = false;
        try { CodexRuntime.ResolveNative(manifest, root, discover, package); }
        catch (NativeRuntimeException error) { rejected = error.Code == "manual-runtime-incomplete"; }
        Check(rejected, "incomplete explicit runtime fails closed");
        Check(activation.RuntimeHealthFor(package, root) == "stale", "incomplete explicit runtime health is stale");

        manifest.RealCodex = b;
        Check(CodexRuntime.ResolveNative(manifest, root, r => { throw new Exception(); }, package) == b, "complete explicit runtime respected without discovery");
        var manual = Bundle(directory, "manual-runtime", "manual-core");
        manifest.RealCodex = manual; manifest.AutomaticRuntime = null; persist();
        File.Delete(Path.Combine(Path.GetDirectoryName(manual), "codex-code-mode-host.exe"));
        rejected = false;
        try { CodexRuntime.ResolveNative(manifest, root, discover, package); }
        catch (NativeRuntimeException error) { rejected = error.Code == "manual-runtime-incomplete"; }
        Check(rejected, "legacy manual runtime outside managed root is never substituted");

        File.Delete(Path.Combine(Path.GetDirectoryName(b), "codex-code-mode-host.exe"));
        manifest.RealCodex = b; manifest.AutomaticRuntime = true; persist();
        rejected = false;
        try { CodexRuntime.ResolveNative(manifest, root, discover, packageNoMatch); }
        catch (NativeRuntimeException error) { rejected = error.Code == "no-compatible-runtime"; }
        Check(rejected, "no complete runtime fails closed without starting stale executable");

        b = Bundle(root, "B", "package-core-B"); var c = Bundle(root, "C", "core-C");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(b, stamp); File.SetLastWriteTimeUtc(c, stamp);
        Check(CodexRuntime.FindUsable(root) == b, "equal timestamp selection is deterministic");
        manifest.RealCodex = a; manifest.AutomaticRuntime = true; persist();
        System.Threading.Tasks.Parallel.For(0, 12, i => { if (CodexRuntime.ResolveNative(manifest, root, CodexRuntime.FindUsable, package) != b) throw new Exception(); });
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
        var processSource = Path.Combine(root, "process-helper.exe");
        File.WriteAllText(processSource, "multi-process fixture");
        File.WriteAllText(processSource+".config", "fixture config");
        var children = new List<Process>();
        try {
            for (int i=0; i<8; i++) children.Add(Process.Start(new ProcessStartInfo(
                typeof(NativeChecks).Assembly.Location, "\""+root+"\" --install \""+processSource+"\"") { UseShellExecute=false, CreateNoWindow=true }));
            bool succeeded = true;
            foreach (var child in children) {
                if (!child.WaitForExit(45000)) { child.Kill(); succeeded=false; }
                else if (child.ExitCode!=0) succeeded=false;
            }
            Check(succeeded && File.ReadAllBytes(NativeActivation.ExpectedHelperPath(processSource)).SequenceEqual(File.ReadAllBytes(processSource))
                && File.ReadAllBytes(NativeActivation.ExpectedHelperPath(processSource)+".config").SequenceEqual(File.ReadAllBytes(processSource+".config")),
                "concurrent processes publish a complete helper bundle");
        } finally { foreach (var child in children) child.Dispose(); }
        var originalEnvironment = LocalEnvironment.Current;
        try {
            // Fits MAX_PATH for the final bundle and short staging name, but not the
            // previous target-name-plus-GUID staging path used on CI.
            var deepRoot = Path.Combine(root, new string('d', Math.Max(1, 132-root.Length)));
            LocalEnvironment.UseIsolated(deepRoot);
            var deepHelper = NativeActivation.InstallHelper(processSource);
            Check(File.ReadAllBytes(deepHelper+".config").SequenceEqual(File.ReadAllBytes(processSource+".config")),
                "deep workspace helper deployment keeps staging paths within Windows limits");
        } finally { LocalEnvironment.Current = originalEnvironment; }
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
    static void BridgeChecks(string directory)
    {
        var log = Path.Combine(LocalEnvironment.Current.DataDirectory, "logs", "native-bridge.log");
        try { if (File.Exists(log)) File.Delete(log); } catch (IOException) { }
        var bridge = new NativeControlBridge(() => new Dictionary<string, NativeRoute>());
        string error;
        Check(bridge.Input("{\"id\":7,\"method\":\"configRequirements/read\"}", out error) != null && error == null, "policy request reaches the bridge");
        bridge.Output("{\"id\":7,\"result\":{\"appServerVersion\":\"0.163.0\",\"configRequirements\":{},\"secret\":\"SHOULD_NOT_APPEAR\"}}");
        var text = File.ReadAllText(log);
        Check(text.Contains("requirements=absent") && text.Contains("appServerVersion=present") && !text.Contains("SHOULD_NOT_APPEAR"),
            "old configRequirements schema is diagnosed without payload leakage");
        Check(bridge.Input("{\"id\":8,\"method\":\"configRequirements/read\"}", out error) != null && error == null, "second policy request reaches the bridge");
        bridge.Output("{\"id\":8,\"result\":{\"appServerVersion\":\"0.164.0\",\"configRequirements\":{\"requirements\":{}}}}");
        text = File.ReadAllText(log);
        Check(text.Contains("requirements=present"), "new configRequirements schema is recorded as present");
        Check(bridge.Input("{\"id\":9,\"method\":\"configRequirements/read\"}", out error) != null && error == null, "error-response policy request reaches the bridge");
        bridge.Output("{\"id\":9,\"error\":{\"code\":-32601,\"message\":\"unsupported\"}}");
        Check(File.ReadAllText(log).Contains("requirements=absent"), "error response is classified without throwing on a missing result");
    }
    static void ProcessChecks(string directory)
    {
        var mock = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MockAppServer.exe");
        if (!File.Exists(mock)) throw new FileNotFoundException("MockAppServer.exe is required for the offline process checks.");
        var runtimeRoot = Path.Combine(directory, "process-runtimes");
        var runtime = Path.Combine(runtimeRoot, "process-runtime"); Directory.CreateDirectory(runtime);
        var core = Path.Combine(runtime, "codex.exe"); File.Copy(mock, core);
        File.WriteAllText(Path.Combine(runtime, "codex-code-mode-host.exe"), "host");
        var package = Path.Combine(directory, "process-package");
        var resources = Path.Combine(package, "app", "resources"); Directory.CreateDirectory(resources);
        File.Copy(mock, Path.Combine(resources, "codex.exe"));
        var home = Path.Combine(directory, "process-home"); Directory.CreateDirectory(home);
        var manifest = Path.Combine(directory, "process-manifest.json");
        File.WriteAllText(manifest, JsonData.Serializer().Serialize(new NativeBridgeSettings {
            RealCodex = core, CodexHome = home, RoutesPath = Path.Combine(directory, "process-routes.json"), AutomaticRuntime = true }));
        var log = Path.Combine(LocalEnvironment.Current.DataDirectory, "logs", "native-bridge.log");
        try { if (File.Exists(log)) File.Delete(log); } catch (IOException) { }
        var exit = NativeControlBridge.Run(manifest, new[] { "app-server", "--mock-exit=7" }, package, runtimeRoot, false);
        Check(exit == 7, "child process exit code is returned to the caller");
        var text = File.ReadAllText(log);
        Check(text.Contains("child exit") && text.Contains("code=7"), "child exit status is diagnosed");
        var truncated = NativeControlBridge.Run(manifest, new[] { "app-server", "--mock-partial" }, package, runtimeRoot, false);
        Check(truncated == 2, "incomplete control-plane frame fails closed");
        Check(text.IndexOf(directory, StringComparison.OrdinalIgnoreCase) < 0, "diagnostics never persist user paths");
    }
    static void AggregationConfigChecks(string directory, string sourceConfig, string sourceCatalog, string ocxConfig)
    {
        var home = Path.Combine(directory, "aggregation-home"); Directory.CreateDirectory(home);
        var config = Path.Combine(home, "config.toml");
        var catalog = Path.Combine(home, "catalog.json"); File.Copy(sourceCatalog, catalog, true);
        var configText = File.ReadAllText(sourceConfig);
        configText = System.Text.RegularExpressions.Regex.Replace(configText, @"(?m)^\s*model_catalog_json\s*=[^\r\n]*", "model_catalog_json = '" + catalog + "'");
        File.WriteAllText(config, configText);
        var paths = new PathSet { CodexHome = home, CodexConfig = config, Catalog = catalog, OcxConfig = ocxConfig };
        AggregationMode.Enable(paths);
        Check(AggregationMode.Enabled && AggregationMode.RouteCount > 0, "aggregation starts with Native provider routes");
        var text = File.ReadAllText(config);
        Check(text.Contains("model_provider = 'launcher_aggregate'") && text.Contains("[model_providers.'launcher_aggregate']") && !text.Contains("launcher_native_PackyAPI"), "aggregation writes one aggregate provider and removes Native block");
        Check(File.ReadAllText(catalog).Contains("launcher-agg-") && !File.ReadAllText(catalog).Contains("launcher-native-"), "aggregation catalog contains aggregate aliases only");
        AggregationMode.Disable(paths);
        Check(!AggregationMode.Enabled && File.ReadAllText(config).Contains("model_provider = 'openai'") && File.ReadAllText(config).Contains("model = \""), "aggregation disable restores direct Codex config");
    }
    static void RouterChecks(string directory)
    {
        CredentialStore.Save(NativeProviders.CredentialId("mock"), "dummy-token");
        var mockPort = ProxyPort.FindAvailable(21001);
        var routerPort = ProxyPort.FindAvailable(22001);
        var mock = new HttpListener(); mock.Prefixes.Add("http://127.0.0.1:" + mockPort + "/"); mock.Start();
        string seenModel = null, seenAuth = null, seenBody = null;
        var received = Task.Run(async () => {
            var context = await mock.GetContextAsync();
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) seenBody = await reader.ReadToEndAsync();
            seenModel = JsonData.Text(JsonData.Parse(seenBody), "model");
            seenAuth = context.Request.Headers["Authorization"];
            var bytes = Encoding.UTF8.GetBytes("data: {\"ok\":true}\n\n");
            context.Response.StatusCode = 200; context.Response.ContentType = "text/event-stream";
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); context.Response.Close();
        });
        var routes = new Dictionary<string, AggregationRoute> { { "launcher-agg-test", new AggregationRoute { Alias = "launcher-agg-test", ProviderId = "mock", Model = "test-model", BaseUrl = "http://127.0.0.1:" + mockPort + "/v1" } } };
        using (var router = new AggregationRouter(routerPort, Path.Combine(directory, "unused-ocx.json"), routes))
        {
            router.Start();
            using (var client = new HttpClient())
            {
                var response = client.PostAsync("http://127.0.0.1:" + routerPort + "/v1/responses", new StringContent("{\"model\":\"launcher-agg-test\",\"input\":[]}", Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
                var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Check(response.IsSuccessStatusCode && text.Contains("data:"), "aggregation router forwards streamed response");
            }
            received.GetAwaiter().GetResult();
        }
        mock.Stop();
        Check(seenModel == "test-model" && seenAuth == "Bearer dummy-token" && seenBody.Contains("test-model") && !seenBody.Contains("launcher-agg-test"), "aggregation router rewrites Packy model and replaces auth");

        var officialPort = ProxyPort.FindAvailable(23001);
        var routerPort2 = ProxyPort.FindAvailable(24001);
        var official = new HttpListener(); official.Prefixes.Add("http://127.0.0.1:" + officialPort + "/"); official.Start();
        string officialAuth = null, officialBody = null;
        var officialTask = Task.Run(async () => {
            var health = await official.GetContextAsync();
            health.Response.StatusCode = 200; health.Response.Close();
            var context = await official.GetContextAsync();
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) officialBody = await reader.ReadToEndAsync();
            officialAuth = context.Request.Headers["Authorization"];
            var bytes = Encoding.UTF8.GetBytes("{\"id\":\"resp-1\",\"output\":[]}");
            context.Response.StatusCode = 200; context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); context.Response.Close();
        });
        var officialConfig = Path.Combine(directory, "official-ocx.json");
        File.WriteAllText(officialConfig, "{\"port\":" + officialPort + "}");
        using (var router = new AggregationRouter(routerPort2, officialConfig, new Dictionary<string, AggregationRoute>()))
        {
            router.Start();
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:" + routerPort2 + "/v1/responses")
                { Content = new StringContent("{\"model\":\"gpt-5.6-luna\",\"input\":[]}", Encoding.UTF8, "application/json") };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "official-token");
                var response = client.SendAsync(request).GetAwaiter().GetResult();
                Check(response.IsSuccessStatusCode && response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("resp-1"), "aggregation router forwards official responses");
            }
            officialTask.GetAwaiter().GetResult();
        }
        official.Stop();
        Check(officialAuth == "Bearer official-token" && officialBody.Contains("gpt-5.6-luna"), "aggregation router preserves official auth and model");
    }
    static void PortChecks(string directory)
    {
        var listener = new TcpListener(IPAddress.Any, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Check(!ProxyPort.IsAvailable(port), "occupied OpenCodex port is detected");
        listener.Stop();
        Check(ProxyPort.IsAvailable(port), "released OpenCodex port is available");
        var free = ProxyPort.FindAvailable(port + 1);
        Check(free > 0 && ProxyPort.IsAvailable(free), "next free OpenCodex port is found");
        var ocx = Path.Combine(directory, "port-config.json");
        File.WriteAllText(ocx, "{\"port\":10100,\"untouched\":true}");
        var store = new ConfigStore();
        store.SetPortAsync(ocx, free).GetAwaiter().GetResult();
        var portRoot = JsonData.Read(ocx);
        Check(Convert.ToInt32(JsonData.Value(portRoot, "port")) == free && Object.Equals(JsonData.Value(portRoot, "untouched"), true), "port update preserves OpenCodex config");
        var codex = Path.Combine(directory, "port-config.toml");
        File.WriteAllText(codex, "model_provider = 'x'\nopenai_base_url = \"http://127.0.0.1:10100/v1\"\n[model_providers.x]\nname='x'\n");
        store.SetProxyRouteAsync(codex, free).GetAwaiter().GetResult();
        var route = File.ReadAllText(codex);
        Check(route.Contains("127.0.0.1:" + free + "/v1") && !route.Contains("10100") && route.Contains("model_provider"), "proxy route update preserves the rest of config.toml");
    }
    static int Main(string[] args)
    {
        try {
            Directory.CreateDirectory(args[0]); LocalEnvironment.UseIsolated(args[0]);
            if (args.Length==3 && args[1]=="--install") { NativeActivation.InstallHelper(args[2]); return 0; }
            RuntimeChecks(args[0]);
            BridgeChecks(args[0]);
            ProcessChecks(args[0]);
            PortChecks(args[0]);
            RouterChecks(args[0]);
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
            var autoEnv=new Dictionary<string,string>();
            var autoActivation=new NativeActivation(Path.Combine(args[0],"auto-helper-update.json"),
                key=>autoEnv.ContainsKey(key)?autoEnv[key]:null,
                (key,value)=>{if(value==null)autoEnv.Remove(key);else autoEnv[key]=value;},()=>{});
            autoActivation.Enable(deployedA,updateManifest,()=>System.Threading.Tasks.Task.FromResult(0)).GetAwaiter().GetResult();
            var currentBuild=typeof(NativeProviders).Assembly.Location;
            Check(autoActivation.AutoUpdateHelper(currentBuild)&&autoActivation.Enabled,"outdated helper updates automatically without enabling Native");
            Check(!autoActivation.AutoUpdateHelper(currentBuild),"automatic helper update is idempotent");
            autoActivation.Disable();
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
            var record = new NativeProviderRecord { Provider = new ProviderOption { Id="mock", DisplayName="Mock", BaseUrl="https://example.invalid/v1", Adapter="openai-responses", RouteMode=ProviderRouteMode.NativeCodex }, Models=new[]{"test-model"} };
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
            File.WriteAllText(config,"model = '"+alias+"'\nmodel_catalog_json = '"+catalog+"'\nopenai_base_url = 'http://127.0.0.1:10100/v1'\n");
            var paths=new PathSet{Codex=Bundle(args[0],"prepare-runtime","prepare-core"),CodexHome=home,CodexConfig=config};
            NativeProviders.Prepare(paths,exe,false).GetAwaiter().GetResult();
            var first=File.ReadAllText(config);
            NativeProviders.Prepare(paths,exe,false).GetAwaiter().GetResult();
            Check(File.ReadAllText(config)==first && first.Contains("openai_base_url = 'http://127.0.0.1:10100/v1'"),"repeated prepare preserves proxy route");
            Check(first.Contains("model_provider = 'launcher_native_mock'")&&!NativeProviders.NeedsProviderRepair(paths),"prepare writes the Native current provider binding");
            Check(File.ReadAllText(catalog).Contains(alias)&&File.Exists(NativeProviders.BridgePath),"prepare writes native catalog aliases and executable manifest");
            AggregationConfigChecks(args[0], config, catalog, Path.Combine(args[0], "aggregation-ocx.json"));
            Check(!first.Contains("dummy-token"),"prepared TOML never contains credential");
            var runtimeB = Bundle(args[0], "prepare-runtime-b", "prepare-core-b");
            var fresh = new PathSet { Codex = runtimeB, AutomaticCodexRuntime = false, CodexHome = home, CodexConfig = config, Catalog = catalog, OcxConfig = paths.OcxConfig };
            NativeProviders.Prepare(fresh, exe, false).GetAwaiter().GetResult();
            var refreshed = JsonData.Serializer().Deserialize<NativeBridgeSettings>(TextFile.Read(NativeProviders.BridgePath));
            Check(refreshed.RealCodex == runtimeB && refreshed.AutomaticRuntime == false, "fresh Native Prepare replaces a stale runtime reference");
            Check(File.ReadAllText(config) == first, "runtime refresh preserves user-edited provider configuration");
            rejected=false;try{NativeProviders.Prepare(paths,exe,true).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&File.ReadAllText(config)==first,"Reserve Force blocks prepare without mutation");
            paths.OcxConfig=Path.Combine(args[0],"proxy.json"); paths.Catalog=catalog;
            var store=new ConfigStore();
            var routeTest=Path.Combine(args[0],"route-preserve.toml");
            File.WriteAllText(routeTest,"model_provider = 'launcher_native_mock'\nopenai_base_url = 'http://127.0.0.1:10100/v1'\n[model_providers.'launcher_native_mock']\nname='Mock'\n");
            store.PrepareProxyRouteAsync(routeTest).GetAwaiter().GetResult();
            Check(File.ReadAllText(routeTest).Contains("model_provider = 'launcher_native_mock'"),"proxy route preparation preserves Native provider binding");
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
        } catch(Exception ex) {
            var aggregate = ex as AggregateException;
            var failure = aggregate == null ? ex : aggregate.Flatten().InnerExceptions[0];
            Console.Error.WriteLine("Native check failed after " + count + " passes: " + failure.GetType().Name + " (HRESULT " + failure.HResult.ToString("X8") + "; payloads suppressed)."); return 1;
        }
    }
}
