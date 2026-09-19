using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    // These records never enter OpenCodex's relay configuration.
    public sealed class NativeProviderRecord
    {
        public ProviderOption Provider { get; set; }
        public string[] Models { get; set; }
        public string[] DiscoveredModels { get; set; }
        public string LastFetch { get; set; }
        public string ConnectionFingerprint { get; set; }
    }
    public sealed class NativeRoute
    {
        public string Provider { get; set; }
        public string Model { get; set; }
        public ProviderRouteMode Mode { get; set; }
    }
    public sealed class NativeBridgeSettings
    {
        public string RealCodex { get; set; }
        public string CodexHome { get; set; }
        public string RoutesPath { get; set; }
        public bool? AutomaticRuntime { get; set; }
    }
    public static class NativeProviders
    {
        static readonly object gate = new object();
        internal static readonly System.Threading.SemaphoreSlim prepareGate = new System.Threading.SemaphoreSlim(1);
        public const string Begin = "# BEGIN OpenCodexLauncher Native Providers v1";
        public const string End = "# END OpenCodexLauncher Native Providers v1";
        public static string StorePath { get { return Path.Combine(LocalEnvironment.Current.DataDirectory, "native-providers.json"); } }
        public static string RoutesPath { get { return Path.Combine(LocalEnvironment.Current.DataDirectory, "native-routes.json"); } }
        public static string BridgePath { get { return Path.Combine(LocalEnvironment.Current.DataDirectory, "native-bridge.json"); } }
        internal static string StatePath { get { return Path.Combine(LocalEnvironment.Current.DataDirectory, "native-sync.json"); } }
        public static string ProviderId(string id) { ModelNames.ValidateProvider(id); return "launcher_native_" + id; }
        public static string CredentialId(string id) { return ProviderId(id); }
        public static string Alias(string id, string model)
        {
            using (var hash = SHA256.Create()) return "launcher-native-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(id + "\n" + model))).Replace("-", "").ToLowerInvariant();
        }
        public static List<NativeProviderRecord> Read()
        {
            if (!File.Exists(StorePath)) return new List<NativeProviderRecord>();
            var records = JsonData.Serializer().Deserialize<List<NativeProviderRecord>>(TextFile.Read(StorePath));
            if (records == null) throw new InvalidDataException("Native provider configuration is invalid.");
            foreach (var record in records) Validate(record);
            if (records.Select(x => x.Provider.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != records.Count) throw new InvalidDataException("Duplicate native provider IDs.");
            return records;
        }
        public static void Validate(NativeProviderRecord record)
        {
            if (record == null || record.Provider == null) throw new InvalidDataException("Native provider is missing.");
            var p = record.Provider; ModelNames.ValidateProvider(p.Id);
            if (p.Id.Equals("openai", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("openai is reserved for Codex login.");
            if (p.RouteMode != ProviderRouteMode.NativeCodex || p.Adapter != "openai-responses") throw new InvalidDataException("Native providers require Responses API.");
            p.BaseUrl = ProviderClient.NormalizeBaseUrl(p.BaseUrl);
            if (!LocalEnvironment.Current.IsIsolated && new Uri(p.BaseUrl).Scheme != "https") throw new InvalidDataException("Native provider URL must use HTTPS.");
            if (String.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.Length > 100 || p.DisplayName.Any(Char.IsControl)) throw new InvalidDataException("Invalid native provider display name.");
            if (record.Models == null) record.Models = new string[0];
            foreach (var model in record.Models) ModelNames.ValidateId(model);
            foreach (var model in record.DiscoveredModels ?? new string[0]) ModelNames.ValidateId(model);
            record.Models = record.Models.Distinct(StringComparer.Ordinal).ToArray();
        }
        public static void Save(NativeProviderRecord record, string editedKey)
        {
            Validate(record);
            lock (gate)
            {
                var records = Read();
                if (records.Any(x => x.Provider.Id != record.Provider.Id && x.Provider.Id.Equals(record.Provider.Id, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Provider IDs cannot differ only by case.");
                records.RemoveAll(x => x.Provider.Id == record.Provider.Id); records.Add(record);
                var credential = CredentialId(record.Provider.Id); var previous = CredentialStore.Snapshot(credential);
                try
                {
                    if (editedKey != null) CredentialStore.Save(credential, editedKey);
                    TextFile.AtomicWrite(StorePath, JsonData.Serializer().Serialize(records), new UTF8Encoding(false));
                }
                catch { if (editedKey != null) CredentialStore.Restore(credential, previous); throw; }
            }
        }
        static string Literal(string value)
        {
            if (value == null || value.IndexOf('\'') >= 0 || value.Any(Char.IsControl)) throw new InvalidDataException("Value cannot be represented safely in native TOML.");
            return "'" + value + "'";
        }
        public static string Block(IEnumerable<NativeProviderRecord> records, string helper)
        {
            if (!File.Exists(helper)) throw new FileNotFoundException("Credential helper is missing.");
            var b = new StringBuilder(Begin + "\n");
            foreach (var r in records)
            {
                Validate(r); var id = ProviderId(r.Provider.Id);
                if (!CredentialStore.Exists(CredentialId(r.Provider.Id))) throw new InvalidDataException("Native credential is missing.");
                b.Append("[model_providers.").Append(Literal(id)).Append("]\nname = ").Append(Literal(r.Provider.DisplayName));
                b.Append("\nbase_url = ").Append(Literal(r.Provider.BaseUrl));
                b.Append("\nwire_api = 'responses'\nrequires_openai_auth = false\nsupports_websockets = false\n");
                b.Append("[model_providers.").Append(Literal(id)).Append(".auth]\ncommand = ").Append(Literal(Path.GetFullPath(helper)));
                b.Append("\nargs = [");
                if (LocalEnvironment.Current.IsIsolated)
                    b.Append(Literal("--credential-isolated")).Append(", ").Append(Literal(Path.GetDirectoryName(LocalEnvironment.Current.DataDirectory))).Append(", ");
                else b.Append("'credential', 'get', ");
                b.Append(Literal(CredentialId(r.Provider.Id))).Append("]\n");
            }
            return b.Append(End).Append('\n').ToString();
        }
        public static string Patch(string original, string previousBlock, string block)
        {
            // Refuse syntax outside this conservative patcher's scope.
            if (original.Contains("\"\"\"") || original.Contains("'''")) throw new InvalidDataException("Native sync does not support multiline TOML strings; no file was changed.");
            int start = original.IndexOf(Begin, StringComparison.Ordinal); string remainder = original;
            if (start >= 0)
            {
                if (String.IsNullOrEmpty(previousBlock) || original.IndexOf(Begin, start + Begin.Length, StringComparison.Ordinal) >= 0 || original.Substring(start) != previousBlock)
                    throw new IOException("Launcher-owned native sections changed externally. Review them before syncing.");
                remainder = original.Substring(0, start);
            }
            else if (!String.IsNullOrEmpty(previousBlock)) throw new IOException("Native sections were removed externally. Review before syncing.");
            if (remainder.Contains("launcher_native_") || remainder.Contains(End)) throw new InvalidDataException("Existing native provider namespace conflicts with Launcher.");
            return remainder + (remainder.EndsWith("\n") || remainder.Length == 0 ? "" : "\n") + block;
        }
        public static async Task Prepare(PathSet paths, string helper, bool reserveForce)
        {
            await prepareGate.WaitAsync();
            try { await PrepareCore(paths, helper, reserveForce); }
            finally { prepareGate.Release(); }
        }
        internal static async Task PrepareCore(PathSet paths, string helper, bool reserveForce, FileTransaction shared = null)
        {
            if (reserveForce) throw new InvalidOperationException("Disable Reserve Force before enabling Native routing.");
            if (!CodexRuntime.IsUsable(paths.Codex) || Path.GetFullPath(paths.Codex).Equals(Path.GetFullPath(helper), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Select a complete Codex CLI runtime including codex-code-mode-host.exe.");
            var records = Read();
            var original = TextFile.Read(paths.CodexConfig);
            bool supported; var keys = DesktopDiagnostics.RootKeys(original, out supported); string catalog;
            if (!supported || !keys.TryGetValue("model_catalog_json", out catalog) || !Path.IsPathRooted(catalog) || !File.Exists(catalog))
                throw new InvalidOperationException("Sync the existing Codex model catalog first, then prepare Native providers.");
            var state = JsonData.Read(StatePath);
            if (state.Count > 0 && JsonData.Text(state, "config") != Path.GetFullPath(paths.CodexConfig)) throw new InvalidOperationException("Native sync is already associated with another Codex home.");
            var block = Block(records, helper); var patched = Patch(original, JsonData.Text(state, "block"), block);
            var catalogBefore = TextFile.Read(catalog);
            var document = JsonData.Parse(catalogBefore);
            var models = JsonData.Array(JsonData.Value(document, "models")).Select(JsonData.Object).ToList();
            if (models.Any(x => x == null)) throw new InvalidDataException("Unsupported catalog format.");
            var oldAliases = new HashSet<string>(JsonData.Array(JsonData.Value(state, "aliases")).OfType<string>(), StringComparer.Ordinal);
            models.RemoveAll(x => oldAliases.Contains(JsonData.Text(x, "slug")));
            var template = models.FirstOrDefault(x => JsonData.Text(x, "slug") == "gpt-5.5") ?? models.FirstOrDefault(x => !JsonData.Text(x, "slug").Contains("/"));
            if (template == null) throw new InvalidDataException("A native Codex catalog template is required.");
            var routes = new Dictionary<string, NativeRoute>(StringComparer.Ordinal);
            foreach (var r in records) foreach (var model in r.Models)
            {
                var alias = Alias(r.Provider.Id, model);
                if (models.Any(x => JsonData.Text(x, "slug") == alias)) throw new InvalidDataException("Model alias conflicts with an existing catalog entry.");
                var entry = JsonData.Parse(JsonData.Serializer().Serialize(template));
                entry["slug"] = alias; entry["display_name"] = r.Provider.DisplayName + " " + model;
                entry["visibility"] = "list"; models.Add(entry);
                routes.Add(alias, new NativeRoute { Provider = ProviderId(r.Provider.Id), Model = model, Mode = ProviderRouteMode.NativeCodex });
            }
            document["models"] = models;
            var transaction = shared ?? new FileTransaction(new[] { paths.CodexConfig, catalog, RoutesPath, BridgePath, StatePath });
            try
            {
                await transaction.Step(() => {
                    if (TextFile.Read(paths.CodexConfig) != original || TextFile.Read(catalog) != catalogBefore)
                        throw new IOException("Configuration changed during Native preparation; retry after reviewing the changes.");
                    TextFile.AtomicWrite(paths.CodexConfig, patched, new UTF8Encoding(false));
                    TextFile.AtomicWrite(catalog, JsonData.Serializer().Serialize(document), new UTF8Encoding(false));
                    TextFile.AtomicWrite(RoutesPath, JsonData.Serializer().Serialize(routes), new UTF8Encoding(false));
                    TextFile.AtomicWrite(BridgePath, JsonData.Serializer().Serialize(new NativeBridgeSettings { RealCodex = paths.Codex, CodexHome = paths.CodexHome, RoutesPath = RoutesPath, AutomaticRuntime = paths.AutomaticCodexRuntime }), new UTF8Encoding(false));
                    TextFile.AtomicWrite(StatePath, JsonData.Serializer().Serialize(new { config = Path.GetFullPath(paths.CodexConfig), block = block, aliases = routes.Keys.ToArray() }), new UTF8Encoding(false));
                    return Task.FromResult(0);
                }); if (shared == null) transaction.Commit();
            }
            catch { if (shared == null) transaction.Rollback(); throw; }
        }
        public static int Credential(string id)
        {
            try
            {
                if (!id.StartsWith("launcher_native_", StringComparison.Ordinal)) return 2;
                var key = CredentialStore.Load(id);
                if (String.IsNullOrWhiteSpace(key) || key.Any(Char.IsControl)) return 3;
                var bytes = Encoding.UTF8.GetBytes(key);
                using (var output = Console.OpenStandardOutput()) { output.Write(bytes, 0, bytes.Length); output.Flush(); }
                return 0;
            }
            catch { return 4; } // No credential content, paths, or exception text on stderr.
        }
    }
}
