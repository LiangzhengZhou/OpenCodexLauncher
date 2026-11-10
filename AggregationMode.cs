using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    public sealed class AggregationRoute
    {
        public string Alias { get; set; }
        public string ProviderId { get; set; }
        public string Model { get; set; }
        public string BaseUrl { get; set; }
    }

    public sealed class AggregationState
    {
        public bool Enabled { get; set; }
        public int Port { get; set; }
        public string ConfigBackup { get; set; }
        public string CatalogBackup { get; set; }
    }

    // A local Responses proxy that keeps Codex on one global provider. Official
    // requests are relayed to the existing OpenCodex endpoint; third-party models
    // are sent directly to their provider with their own DPAPI credential.
    public sealed class AggregationRouter : IDisposable
    {
        readonly HttpListener listener;
        readonly string ocxConfig;
        readonly Dictionary<string, AggregationRoute> routes;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly HttpClient client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(10) };
        public int Port { get; private set; }

        public AggregationRouter(int port, string ocxConfig, Dictionary<string, AggregationRoute> routes)
        {
            Port = port; this.ocxConfig = ocxConfig; this.routes = routes ?? new Dictionary<string, AggregationRoute>(StringComparer.Ordinal);
            listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        }
        public void Start()
        {
            listener.Start();
            Task.Run(() => Loop());
        }
        async Task Loop()
        {
            while (!life.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }
                if (life.IsCancellationRequested) return;
                Task.Run(() => Handle(context));
            }
        }
        async Task Handle(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/healthz")
                {
                    context.Response.StatusCode = 200; context.Response.ContentType = "text/plain";
                    var ok = Encoding.UTF8.GetBytes("ok"); await context.Response.OutputStream.WriteAsync(ok, 0, ok.Length); context.Response.Close(); return;
                }
                if (context.Request.HttpMethod != "POST" || context.Request.Url.AbsolutePath != "/v1/responses")
                { context.Response.StatusCode = 404; context.Response.Close(); return; }
                var body = await ReadBody(context.Request.InputStream).ConfigureAwait(false);
                if (body == null) { context.Response.StatusCode = 413; context.Response.Close(); return; }
                var document = JsonData.Parse(body);
                var model = JsonData.Text(document, "model");
                if (String.IsNullOrWhiteSpace(model)) { context.Response.StatusCode = 400; context.Response.Close(); return; }

                AggregationRoute route;
                var providerModel = model;
                var upstream = (string)null;
                var packy = routes.TryGetValue(model, out route) && route != null;
                if (model.StartsWith("launcher-agg-", StringComparison.Ordinal) && !packy)
                { context.Response.StatusCode = 400; context.Response.Close(); return; }
                if (packy)
                {
                    providerModel = route.Model;
                    document["model"] = providerModel;
                    body = JsonData.Serializer().Serialize(document);
                    upstream = route.BaseUrl.TrimEnd('/') + "/responses";
                }
                else
                {
                    var endpoint = await OpenCodexEndpointResolver.ResolveAsync(ocxConfig, life.Token).ConfigureAwait(false);
                    upstream = endpoint.DataUrl + "/responses";
                }

                using (var request = new HttpRequestMessage(HttpMethod.Post, upstream))
                {
                    request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                    CopyRequestHeaders(context.Request, request, packy);
                    if (packy) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CredentialStore.Load(NativeProviders.CredentialId(route.ProviderId)));
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, life.Token).ConfigureAwait(false))
                    {
                        context.Response.StatusCode = (int)response.StatusCode;
                        CopyResponseHeaders(response, context.Response);
                        context.Response.SendChunked = true;
                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var target = context.Response.OutputStream)
                        {
                            var buffer = new byte[16384]; int read;
                            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, life.Token).ConfigureAwait(false)) > 0)
                            { await target.WriteAsync(buffer, 0, read, life.Token).ConfigureAwait(false); await target.FlushAsync(life.Token).ConfigureAwait(false); }
                        }
                    }
                }
                context.Response.Close();
            }
            catch (OperationCanceledException) { try { context.Response.Abort(); } catch { } }
            catch (Exception error)
            {
                LogFailure("route", error);
                try { context.Response.StatusCode = 502; context.Response.Close(); } catch { }
            }
        }
        static async Task<string> ReadBody(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                var bytes = new byte[16384]; int read;
                while ((read = await stream.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > 64L * 1024 * 1024) return null;
                    buffer.Write(bytes, 0, read);
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
        }
        static void CopyRequestHeaders(HttpListenerRequest source, HttpRequestMessage target, bool packy)
        {
            foreach (var key in source.Headers.AllKeys)
            {
                if (String.Equals(key, "Host", StringComparison.OrdinalIgnoreCase) || String.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || String.Equals(key, "Connection", StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase) || String.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase) && packy)
                    continue;
                var value = source.Headers[key];
                try { if (!target.Headers.TryAddWithoutValidation(key, value)) target.Content.Headers.TryAddWithoutValidation(key, value); } catch (ArgumentException) { }
            }
        }
        static void CopyResponseHeaders(HttpResponseMessage source, HttpListenerResponse target)
        {
            if (source.Content.Headers.ContentType != null) target.ContentType = source.Content.Headers.ContentType.ToString();
            foreach (var header in source.Headers)
            {
                if (String.Equals(header.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || String.Equals(header.Key, "Connection", StringComparison.OrdinalIgnoreCase)) continue;
                try { target.Headers[header.Key] = String.Join(",", header.Value); } catch (ArgumentException) { }
            }
            foreach (var header in source.Content.Headers)
            {
                if (String.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase) || String.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                try { target.Headers[header.Key] = String.Join(",", header.Value); } catch (ArgumentException) { }
            }
        }
        static void LogFailure(string stage, Exception error)
        {
            try
            {
                var directory = Path.Combine(LocalEnvironment.Current.DataDirectory, "logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "aggregation.log"),
                    DateTime.UtcNow.ToString("o") + " stage=" + stage + " type=" + error.GetType().Name + " hresult=0x" + error.HResult.ToString("X8") + Environment.NewLine);
            }
            catch { }
        }
        public void Dispose()
        {
            life.Cancel();
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
            client.Dispose();
        }
    }

    public static class AggregationMode
    {
        const string Begin = "# BEGIN OpenCodexLauncher Aggregation v1";
        const string End = "# END OpenCodexLauncher Aggregation v1";
        static readonly object gate = new object();
        static AggregationRouter router;
        public static string StatePath { get { return Path.Combine(LocalEnvironment.Current.DataDirectory, "aggregation-state.json"); } }
        public static string RoutesPath { get { return Path.Combine(LocalEnvironment.Current.DataDirectory, "aggregation-routes.json"); } }
        public static bool Enabled { get { try { return ReadState() != null; } catch { return false; } } }
        public static int Port { get { var s = ReadState(); return s == null ? 0 : s.Port; } }
        public static int RouteCount { get { try { return ReadRoutes().Count; } catch { return 0; } } }
        static AggregationState ReadState()
        {
            if (!File.Exists(StatePath)) return null;
            var state = JsonData.Serializer().Deserialize<AggregationState>(TextFile.Read(StatePath));
            return state != null && state.Enabled ? state : null;
        }
        public static string Alias(string providerId, string model) { return "launcher-agg-" + NativeProviders.Alias(providerId, model).Substring("launcher-native-".Length); }
        public static Dictionary<string, AggregationRoute> BuildRoutes(IEnumerable<NativeProviderRecord> records)
        {
            var routes = new Dictionary<string, AggregationRoute>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (record == null || record.Provider == null || record.Models == null) continue;
                if (!String.IsNullOrWhiteSpace(record.Provider.BaseUrl) && !record.Provider.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var model in record.Models)
                {
                    var alias = Alias(record.Provider.Id, model);
                    routes[alias] = new AggregationRoute { Alias = alias, ProviderId = record.Provider.Id, Model = model, BaseUrl = record.Provider.BaseUrl };
                }
            }
            return routes;
        }
        public static void Enable(PathSet paths)
        {
            if (String.IsNullOrWhiteSpace(paths.OcxConfig) || String.IsNullOrWhiteSpace(paths.CodexConfig)) throw new InvalidOperationException("Setup is incomplete.");
            if (Enabled) return;
            if (NativeActivation.Current.Enabled) NativeActivation.Current.Disable();
            lock (gate)
            {
                if (router != null) return;
                var routes = BuildRoutes(NativeProviders.Read());
                var port = ProxyPort.FindAvailable(10201);
                if (port == 0) throw new InvalidOperationException("No free aggregation port.");
                var backupDir = Path.Combine(LocalEnvironment.Current.DataDirectory, "aggregation-backup");
                Directory.CreateDirectory(backupDir);
                var configBackup = Path.Combine(backupDir, "config.toml");
                var catalogBackup = Path.Combine(backupDir, "catalog.json");
                var catalog = CatalogPath(paths.CodexConfig);
                WriteDirectBackup(paths.CodexConfig, catalog, configBackup, catalogBackup);
                try
                {
                    ApplyConfig(paths.CodexConfig, port, catalog, routes);
                    WriteRoutes(routes);
                    router = new AggregationRouter(port, paths.OcxConfig, routes); router.Start();
                    var state = new AggregationState { Enabled = true, Port = port, ConfigBackup = configBackup, CatalogBackup = catalogBackup };
                    TextFile.AtomicWrite(StatePath, JsonData.Serializer().Serialize(state), new UTF8Encoding(false));
                }
                catch
                {
                    try { if (router != null) router.Dispose(); } catch { } router = null;
                    try { if (File.Exists(configBackup)) File.Copy(configBackup, paths.CodexConfig, true); } catch { }
                    try { if (catalog != null && File.Exists(catalogBackup)) File.Copy(catalogBackup, catalog, true); } catch { }
                    throw;
                }
            }
        }
        public static void Disable(PathSet paths)
        {
            lock (gate)
            {
                var state = ReadState();
                try { if (router != null) { router.Dispose(); router = null; } } catch { }
                if (state == null) return;
                try
                {
                    if (!String.IsNullOrWhiteSpace(state.ConfigBackup) && File.Exists(state.ConfigBackup) && !String.IsNullOrWhiteSpace(paths.CodexConfig))
                        File.Copy(state.ConfigBackup, paths.CodexConfig, true);
                    var catalog = paths.CodexConfig == null ? null : CatalogPath(paths.CodexConfig);
                    if (catalog != null && !String.IsNullOrWhiteSpace(state.CatalogBackup) && File.Exists(state.CatalogBackup)) File.Copy(state.CatalogBackup, catalog, true);
                }
                finally { try { File.Delete(StatePath); } catch { } }
            }
        }
        public static void Resume(PathSet paths)
        {
            var state = ReadState(); if (state == null) return;
            var routes = ReadRoutes(); if (routes.Count == 0) return;
            lock (gate)
            {
                if (router != null) return;
                router = new AggregationRouter(state.Port, paths.OcxConfig, routes); router.Start();
            }
        }
        public static void Stop()
        {
            lock (gate) { if (router != null) { router.Dispose(); router = null; } }
        }
        static string CatalogPath(string config)
        {
            try { bool supported; var keys = DesktopDiagnostics.RootKeys(TextFile.Read(config), out supported); string path; return supported && keys.TryGetValue("model_catalog_json", out path) && Path.IsPathRooted(path) ? path : null; }
            catch { return null; }
        }
        static void WriteDirectBackup(string configPath, string catalogPath, string configBackup, string catalogBackup)
        {
            var text = RemoveBlock(RemoveBlock(TextFile.Read(configPath), NativeProviders.Begin, NativeProviders.End), Begin, End);
            var firstTable = System.Text.RegularExpressions.Regex.Match(text, @"(?m)^\s*\[");
            var rootEnd = firstTable.Success ? firstTable.Index : text.Length;
            var root = text.Substring(0, rootEnd); var rest = text.Substring(rootEnd);
            root = System.Text.RegularExpressions.Regex.Replace(root, @"(?m)^\s*model_provider\s*=.*(?:\r?\n|$)", "");
            root = System.Text.RegularExpressions.Regex.Replace(root, @"(?m)^\s*model\s*=.*(?:\r?\n|$)", "");
            var official = OfficialModel(catalogPath) ?? "gpt-5.5";
            root = "model_provider = 'openai'" + Environment.NewLine + "model = \"" + official + "\"" + Environment.NewLine + root;
            TextFile.AtomicWrite(configBackup, root + rest, new UTF8Encoding(false));
            if (catalogPath == null || !File.Exists(catalogPath)) return;
            var document = JsonData.Parse(TextFile.Read(catalogPath));
            document["models"] = JsonData.Array(JsonData.Value(document, "models")).Where(x => {
                var slug = JsonData.Text(JsonData.Object(x), "slug");
                return !slug.StartsWith("launcher-native-", StringComparison.Ordinal) && !slug.StartsWith("launcher-agg-", StringComparison.Ordinal);
            }).ToArray();
            TextFile.AtomicWrite(catalogBackup, JsonData.Serializer().Serialize(document), new UTF8Encoding(false));
        }
        static void WriteRoutes(Dictionary<string, AggregationRoute> routes)
        {
            TextFile.AtomicWrite(RoutesPath, JsonData.Serializer().Serialize(routes), new UTF8Encoding(false));
        }
        static Dictionary<string, AggregationRoute> ReadRoutes()
        {
            try { return JsonData.Serializer().Deserialize<Dictionary<string, AggregationRoute>>(TextFile.Read(RoutesPath)) ?? new Dictionary<string, AggregationRoute>(); }
            catch { return new Dictionary<string, AggregationRoute>(); }
        }
        static void ApplyConfig(string configPath, int port, string catalogPath, Dictionary<string, AggregationRoute> routes)
        {
            var original = TextFile.Read(configPath);
            var withoutNative = RemoveBlock(original, NativeProviders.Begin, NativeProviders.End);
            var withoutAggregate = RemoveBlock(withoutNative, Begin, End);
            var firstTable = System.Text.RegularExpressions.Regex.Match(withoutAggregate, @"(?m)^\s*\[");
            var rootEnd = firstTable.Success ? firstTable.Index : withoutAggregate.Length;
            var root = withoutAggregate.Substring(0, rootEnd); var rest = withoutAggregate.Substring(rootEnd);
            root = System.Text.RegularExpressions.Regex.Replace(root, @"(?m)^\s*model_provider\s*=.*(?:\r?\n|$)", "");
            root = System.Text.RegularExpressions.Regex.Replace(root, @"(?m)^\s*model\s*=.*(?:\r?\n|$)", "");
            var official = OfficialModel(catalogPath);
            root = "model_provider = 'launcher_aggregate'" + Environment.NewLine + "model = \"" + (official ?? "gpt-5.5") + "\"" + Environment.NewLine + root;
            var block = Begin + Environment.NewLine +
                "[model_providers.'launcher_aggregate']\nname = 'OpenCodex Aggregate'\nbase_url = 'http://127.0.0.1:" + port + "/v1'\nwire_api = 'responses'\nrequires_openai_auth = true\nsupports_websockets = false\n" + End + Environment.NewLine;
            TextFile.AtomicWrite(configPath, root + rest + (rest.EndsWith("\n") || rest.Length == 0 ? "" : "\n") + block, new UTF8Encoding(false));
            if (catalogPath != null) WriteCatalog(catalogPath, routes);
        }
        static string RemoveBlock(string text, string begin, string end)
        {
            var start = text.IndexOf(begin, StringComparison.Ordinal);
            if (start < 0) return text;
            var finish = text.IndexOf(end, start, StringComparison.Ordinal);
            if (finish < 0) throw new InvalidDataException("Unclosed Launcher-owned configuration block.");
            finish += end.Length;
            if (finish < text.Length && text[finish] == '\r') finish++;
            if (finish < text.Length && text[finish] == '\n') finish++;
            return text.Substring(0, start) + text.Substring(finish);
        }
        static string OfficialModel(string catalogPath)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(catalogPath) || !File.Exists(catalogPath)) return null;
                var root = JsonData.Parse(TextFile.Read(catalogPath));
                return JsonData.Array(JsonData.Value(root, "models")).Select(JsonData.Object)
                    .Select(x => JsonData.Text(x, "slug", "id")).FirstOrDefault(x => !String.IsNullOrWhiteSpace(x) && !x.StartsWith("launcher-", StringComparison.Ordinal) && !x.StartsWith("ccs-", StringComparison.Ordinal));
            }
            catch { return null; }
        }
        static void WriteCatalog(string catalogPath, Dictionary<string, AggregationRoute> routes)
        {
            var root = JsonData.Parse(TextFile.Read(catalogPath));
            var models = JsonData.Array(JsonData.Value(root, "models")).Select(JsonData.Object).Where(x => x != null)
                .Where(x => !JsonData.Text(x, "slug").StartsWith("launcher-native-", StringComparison.Ordinal) && !JsonData.Text(x, "slug").StartsWith("launcher-agg-", StringComparison.Ordinal)).ToList();
            var template = models.FirstOrDefault(x => JsonData.Text(x, "slug") == "gpt-5.5") ?? models.FirstOrDefault();
            if (template == null) throw new InvalidDataException("A native catalog template is required.");
            foreach (var route in routes.Values)
            {
                var entry = JsonData.Parse(JsonData.Serializer().Serialize(template));
                entry["slug"] = route.Alias; entry["display_name"] = "PA " + route.Model; entry["visibility"] = "list";
                models.Add(entry);
            }
            root["models"] = models;
            TextFile.AtomicWrite(catalogPath, JsonData.Serializer().Serialize(root), new UTF8Encoding(false));
        }
    }
}
