using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCodexLauncherV2;

class RegressionChecks
{
    static int passed;
    static string root;
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
    static string Write(string name, string content) { var path = Path.Combine(root, name); File.WriteAllText(path, content); return path; }
    static Dictionary<string, object> Provider(string file, string id) { return JsonData.Object(JsonData.Value(JsonData.Object(JsonData.Value(JsonData.Read(file), "providers")), id)); }
    static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } catch (LauncherError) { rejected = true; } Check(rejected, name); }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "startup-failure") { Console.Error.WriteLine("fixture import failed; api_key=dummy-startup-secret"); return 17; }
        if (args.Length > 0 && args[0] == "startup-home") { return Directory.Exists(Environment.GetEnvironmentVariable("CODEX_HOME")) && Directory.Exists(Environment.GetEnvironmentVariable("OPENCODEX_HOME")) ? 0 : 19; }
        // Use this executable as the process-tree fixture, without shell startup dependencies.
        if (args.Length > 0 && args[0] == "installer-child") { Thread.Sleep(60000); return 0; }
        if (args.Length > 1 && args[0] == "installer-parent")
        {
            using (var child = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "installer-child") { UseShellExecute=false, CreateNoWindow=true }))
            { File.WriteAllText(args[1], child.Id.ToString()); Thread.Sleep(60000); }
            return 0;
        }
        if (args.Length > 0 && args[0] == "echo-args") { Console.WriteLine(JsonData.Serializer().Serialize(args.Skip(1).ToArray())); return 0; }
        if (args.Length > 0 && args[0] == "output") { for (var i = 0; i < 4000; i++) { Console.WriteLine("OUT-" + i); Console.Error.WriteLine("ERR-" + i); } return 0; }
        root = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        LocalEnvironment.UseIsolated(Path.Combine(root, "isolated"));
        try { Run().GetAwaiter().GetResult(); Console.WriteLine("ALL " + passed + " CHECKS PASSED"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(Redactor.Apply(ex.ToString())); return 1; }
    }
    static async Task Run()
    {
        var store = new ConfigStore();
        var config = Write("config.json", "{\"defaultProvider\":\"demo-provider\",\"providers\":{\"openai\":{\"adapter\":\"openai-responses\",\"baseUrl\":\"https://chatgpt.com/backend-api/codex\"},\"demo-provider\":{\"adapter\":\"openai-chat\",\"baseUrl\":\"https://example.invalid/v1\",\"displayName\":\"Old\",\"headers\":{\"X-Example\":\"dummy\"},\"timeout\":123},\"other\":{\"baseUrl\":\"https://example.invalid\"}},\"customModels\":[{\"id\":\"old\",\"provider\":\"demo-provider\",\"modelId\":\"gpt-5.6-sol\",\"contextWindow\":10000},{\"id\":\"other\",\"provider\":\"other\",\"modelId\":\"keep-me\"}]}");
        var officialBefore = JsonData.Serializer().Serialize(Provider(config, "openai"));
        var p = new ProviderOption { Id="demo-provider", DisplayName="Demo Provider", BaseUrl="https://example.invalid/v1/responses", Adapter="openai-chat" };
        await store.UpsertProviderAsync(config, p, "");
        var saved = Provider(config, "demo-provider");
        Check(saved.ContainsKey("headers") && saved.ContainsKey("timeout"), "provider extra settings preserved");
        Check(JsonData.Text(saved,"displayName") == "Demo Provider", "provider display name saved");
        Check(JsonData.Text(saved,"adapter") == "openai-chat", "protocol preserved");
        Check(JsonData.Text(saved,"baseUrl") == "https://example.invalid/v1", "request endpoint normalized to base URL");
        Check(JsonData.Serializer().Serialize(Provider(config,"openai")) == officialBefore, "official provider unchanged");
        var catalog = Write("catalog.json", "{\"models\":[{\"slug\":\"gpt-6-astra\",\"display_name\":\"GPT-6 Astra\",\"visibility\":\"list\"}]}");
        var paths = new PathSet { Catalog=catalog, OcxConfig=config };
        Check(CatalogReader.Load(paths,store).Any(x=>x.Id=="gpt-6-astra" && x.IsNative), "JSON catalog reads official Astra");
        Check(CatalogReader.Load(paths,store).Any(x=>x.Id=="other/keep-me"), "JSON custom models read correctly");
        Check(ProviderClient.ParseModels("{\"data\":[{\"id\":\"gpt-6-astra\"},{\"id\":\"vendor/model\"}]}").Count==2,"OpenAI-compatible data array read");
        await store.SelectProviderModelsAsync(config,"demo-provider","Demo Provider",new[]{"gpt-6-astra"},new[]{"gpt-6-astra","gpt-5.6-sol","vendor/model"});
        var text=File.ReadAllText(config); var list=JsonData.Array(JsonData.Value(JsonData.Read(config),"customModels")).Select(JsonData.Object).ToList();
        Check(list.Any(x=>JsonData.Text(x,"modelId")=="keep-me"),"other provider models preserved");
        Check(list.Any(x=>JsonData.Text(x,"modelId")=="gpt-5.6-sol" && x.ContainsKey("contextWindow")),"unselected model metadata retained");
        Check(list.Any(x=>JsonData.Text(x,"displayName")=="Demo Provider GPT-6 Astra"),"third-party Astra label persisted");
        Check(store.SelectedModels(config,"demo-provider").SetEquals(new[]{"gpt-6-astra"}),"selected model allowlist exact");
        var providerWithDiscovery = Provider(config, "demo-provider");
        Check(JsonData.Array(JsonData.Value(providerWithDiscovery, "discoveredModels")).OfType<string>().Count() == 3, "discovered provider models persisted");
        Check(JsonData.Array(JsonData.Value(providerWithDiscovery, "discoveredModels")).OfType<string>().Contains("vendor/model"), "unselected discovered model remains reselectable");
        var models=CatalogReader.Load(paths,store);
        Check(models.Any(x=>x.Id=="gpt-6-astra") && models.Any(x=>x.Id=="demo-provider/gpt-6-astra"),"official and third-party Astra coexist");
        Check(!models.Any(x=>x.Id=="demo-provider/gpt-5.6-sol"),"unselected provider model hidden");
        File.Copy(config,Path.Combine(root,"upstream-fixture.json"),true);
        await store.SelectProviderModelsAsync(config,"demo-provider","Demo Provider",new string[0],new[]{"gpt-6-astra"});
        Check((bool)Provider(config,"demo-provider")["disabled"],"zero selection disables provider instead of exposing all");
        Check(CatalogReader.Load(paths,store).Any(x=>x.Id=="gpt-6-astra"),"zero selection preserves official Astra");
        await store.AddCustomModelsAsync(config,"demo-provider",new[]{"gpt-6-astra","gpt-5.6-sol"});
        Check(store.SelectedModels(config,"demo-provider").Count==2,"reselect/add merges models");
        Check(JsonData.Array(JsonData.Value(JsonData.Read(config),"customModels")).Count==3,"repeated import does not duplicate entries");
        Reject(()=>store.SelectProviderModelsAsync(config,"openai","Official",new[]{"gpt-6-astra"},new[]{"gpt-6-astra"}),"official provider cannot be replaced by third-party picker");
        Reject(()=>store.UpsertProviderAsync(config,new ProviderOption{Id="openai",BaseUrl="https://example.invalid",Adapter="openai-responses"},""),"official credentials protected from provider editor");
        Reject(()=>ModelNames.ValidateId("foo\" & echo injected"),"command injection model id rejected");
        Reject(()=>ModelNames.ValidateId("foo\nbar"),"control characters rejected");
        Reject(()=>ProviderClient.NormalizeBaseUrl("https://example.invalid/v1?api_key=dummy"),"credentials in URL rejected");
        Check(CredentialStore.EnvName("a-b")!=CredentialStore.EnvName("a.b"),"provider credential names do not collide");
        var redacted=Redactor.Apply("{\"apiKey\":\"DUMMY_API_VALUE\",\"nested\":{\"access_token\":\"DUMMY_ACCESS\"},\"rows\":[{\"refresh_token\":\"DUMMY_REFRESH\"}]}");
        Check(!redacted.Contains("DUMMY_"),"nested JSON credentials redacted");
        Check(!Redactor.Apply("Authorization: Bearer DUMMY_ACCESS").Contains("DUMMY_ACCESS"),"Bearer credential redacted");
        var toml=Write("config.toml","model_provider = \"custom\"\nmodel = \"gpt-5.6-sol\"\n[other]\nvalue = 1\n");
        await store.SetReserveForceAsync(config, "demo-provider/gpt-6-astra", true);
        Check(store.ReserveForceTargetRoute(config) == "demo-provider/gpt-6-astra", "reserve force pre-route target persists");
        await store.SetReserveForceAsync(config, "", false);
        Check(store.ReserveForceTargetRoute(config) == "", "reserve force pre-route can be disabled");
        await store.SetBlockedModelRedirectAsync(config, "gpt-reserve", "demo-provider/gpt-6-astra", true);
        Check(store.BlockedModelRedirect(config, "gpt-reserve") == "demo-provider/gpt-6-astra", "reserve redirect persists exact target");
        await store.SetBlockedModelRedirectAsync(config, "other-reserve", "other/model", true);
        await store.SetBlockedModelRedirectAsync(config, "gpt-reserve", "", false);
        Check(store.BlockedModelRedirect(config, "gpt-reserve") == "", "reserve redirect can be disabled");
        Check(store.BlockedModelRedirect(config, "other-reserve") == "other/model", "disabling reserve redirect preserves unrelated redirects");
        await store.SetCodexIntegrationAsync(config, true);
        Check(JsonData.Object(JsonData.Value(JsonData.Read(config), "clientIntegrations"))["codex"].Equals(true), "Codex integration enabled");
        await store.PrepareProxyRouteAsync(toml);
        Check(!File.ReadAllText(toml).Contains("model_provider = \"custom\""), "external Codex route cleared for proxy sync");
        await store.SetDefaultModelAsync(toml,"demo-provider/gpt-6-astra");
        Check(store.CurrentModel(toml)=="demo-provider/gpt-6-astra" && File.ReadAllText(toml).Contains("[other]"),"default model update preserves TOML sections");
        Reject(()=>store.SetDefaultModelAsync(toml,"fixture\\new"),"TOML escape injection rejected");
        var runner=new AsyncProcessRunner(); var exe=Process.GetCurrentProcess().MainModule.FileName;
        var output=await runner.RunAsync(exe,"output",root,TimeSpan.FromSeconds(15),CancellationToken.None,null);
        Check(output.Succeeded && output.Output.Contains("OUT-3999") && output.Error.Contains("ERR-3999"),"process drains stdout and stderr to EOF");
        var values=new[]{"space value","quote\"test","amp&echo", "percent%PATH%", "trailing\\", "vendor/model"};
        var echoed=await runner.RunAsync(exe,"echo-args "+String.Join(" ",values.Select(Commands.Quote)),root,TimeSpan.FromSeconds(15),CancellationToken.None,null);
        Check(echoed.Succeeded && JsonData.Serializer().Deserialize<string[]>(echoed.Output).SequenceEqual(values),"direct process argv preserves metacharacters without shell evaluation");
        await HttpCheck();
        Check(Directory.GetFiles(root,"config.json.bak.*").Length>=4,"rapid writes get unique backups");
        var routed = new LauncherModel { Id = "demo-provider/gpt-6-astra", RouteId = "demo-provider/gpt-6-astra", ProviderId = "demo-provider", DisplayName = "Demo Provider GPT-6 Astra", Routed = true, Availability = "available" };
        var forcePlan = new RuntimeResolver().Resolve(new LaunchRequest { ProjectPath = root, SelectedModel = routed, Strategy = LaunchStrategy.Force, PreferredRuntime = RuntimeKind.CodexCli });
        Check(forcePlan.AllowRuntimeFallback && !forcePlan.AllowModelFallback, "force plan allows runtime fallback only");
        Check(forcePlan.Candidates.Count >= 2 && forcePlan.Candidates.All(x => x.ModelRouteId == routed.RouteId), "force candidates preserve exact route");
        var claudeFirst = new RuntimeResolver().Resolve(new LaunchRequest { ProjectPath = root, SelectedModel = routed, Strategy = LaunchStrategy.Force, PreferredRuntime = RuntimeKind.ClaudeCode });
        Check(claudeFirst.Candidates[0].Runtime == RuntimeKind.ClaudeCode && claudeFirst.Candidates[1].Runtime == RuntimeKind.CodexCli, "preferred runtime remains first with fallback");
        var followPlan = new RuntimeResolver().Resolve(new LaunchRequest { ProjectPath = root, SelectedModel = routed, Strategy = LaunchStrategy.FollowCodex, PreferredRuntime = RuntimeKind.CodexCli });
        Check(followPlan.Candidates.Count == 1 && followPlan.Candidates[0].Runtime == RuntimeKind.CodexDesktop, "follow-codex keeps desktop runtime");
        RouteVerifier.AssertModelInvariant(routed.RouteId, routed.RouteId);
        Reject(()=>RouteVerifier.AssertModelInvariant(routed.RouteId, "gpt-reserve"),"route invariant rejects reserve override");
        var portConfig = Write("ocx-config.json", "{\"port\":12345}");
        File.WriteAllText(Path.Combine(root, "runtime-port.json"), "{\"port\":23456}");
        Check(OpenCodexEndpointResolver.Candidates(portConfig).Take(2).SequenceEqual(new[]{23456,12345}), "dynamic OpenCodex port candidates prefer runtime info");
         var fakePackage = Path.Combine(root, "fake-opencodex"); Directory.CreateDirectory(Path.Combine(fakePackage, "bin")); Directory.CreateDirectory(Path.Combine(fakePackage, "src")); var fakeOcx = Path.Combine(fakePackage, "bin", "ocx.mjs"); File.WriteAllText(fakeOcx, ""); var fakeRouter = Path.Combine(fakePackage, "src", "router.ts"); File.WriteAllText(fakeRouter, "  const route = routeModelInternal(config, modelId, false, policyEvidence);\n  const route = routeModelInternal(config, modelId, false, policyEvidence, true);\n"); var patchResult = OpenCodexCompatibility.EnsureReservePreRouting(fakeOcx); Check(File.ReadAllText(fakeRouter).Contains("reserve pre-route patch"), "reserve pre-routing compatibility patch applies"); var patchedHash = FileTransaction.Hash(fakeRouter); var backups = Directory.GetFiles(Path.GetDirectoryName(fakeRouter), "*.launcher-bak.*").Length; OpenCodexCompatibility.EnsureReservePreRouting(fakeOcx); Check(FileTransaction.Hash(fakeRouter) == patchedHash && Directory.GetFiles(Path.GetDirectoryName(fakeRouter), "*.launcher-bak.*").Length == backups, "reserve pre-routing compatibility patch is idempotent");
        await ReleaseChecks.Run(root, Check);
        await InstallerChecks.Run(root, Check);
        await UpdaterChecks.Run(root, Check);
        await DesktopDiagnosticChecks.Run(root, Check);
        await TransportDiagnosticChecks.Run(Check);
        await DesktopSyncChecks.Run(root, Check);
    }
    static async Task HttpCheck()
    {
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port;
        var server=Task.Run(()=>{
            using(var client=listener.AcceptTcpClient()) using(var stream=client.GetStream()) using(var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true)) {
                while(!String.IsNullOrEmpty(reader.ReadLine())){}
                var body="{\"data\":[{\"id\":\"gpt-6-astra\"},{\"id\":\"gpt-5.6-sol\"}]}";
                var bytes=Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n"+body); stream.Write(bytes,0,bytes.Length);
            }
        });
        try { var found=await ProviderClient.FetchModelsAsync(new ProviderOption{BaseUrl="http://127.0.0.1:"+port},"",CancellationToken.None); Check(found.Count==2 && found.Contains("gpt-6-astra"),"HTTP model discovery end-to-end"); await server; }
        finally { listener.Stop(); }
    }
}

