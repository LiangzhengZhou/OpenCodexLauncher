using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    public sealed class DesktopConfigCandidate
    {
        public string Role, ConfigPath, ContentHash;
    }
    public static class DesktopCandidates
    {
        // Explicit-click discovery only. No recursive account or installation search.
        public static List<DesktopConfigCandidate> Find(LauncherSettings settings, PathSet paths, DesktopDiagnosticInputs input)
        {
            var result = new List<DesktopConfigCandidate>();
            if (!settings.SetupCompleted) return result;
            var homes = new[] { settings.DesktopConfigPath, Combine(input.EnvironmentHome), Combine(input.DefaultHome), paths.CodexConfig };
            var roles = new[] { "associated", "environment", "default", "target" };
            for (int i = 0; i < homes.Length; i++)
            {
                try
                {
                    var file = homes[i]; if (String.IsNullOrWhiteSpace(file) || !Path.IsPathRooted(file)) continue;
                    file = Path.GetFullPath(file);
                    if (result.Any(x => String.Equals(x.ConfigPath, file, StringComparison.OrdinalIgnoreCase))) continue;
                    var text = input.Read(file); if (text == null) continue;
                    bool supported; DesktopDiagnostics.RootKeys(text, out supported);
                    if (supported) result.Add(new DesktopConfigCandidate { Role = roles[i], ConfigPath = file, ContentHash = Digest(text) });
                }
                catch { /* Unreadable candidates are not eligible for automatic association. */ }
            }
            return result;
        }
        static string Combine(string home) { try { return String.IsNullOrWhiteSpace(home) ? null : Path.Combine(home, "config.toml"); } catch { return null; } }
        static string Digest(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)));
        }
        public static LauncherSettings Confirm(LauncherSettings current, DesktopConfigCandidate candidate, DesktopDiagnosticInputs input)
        {
            if (!current.SetupCompleted || candidate == null) throw new InvalidOperationException(L.M("desktop.setup"));
            var text = input.Read(candidate.ConfigPath);
            if (text == null || Digest(text) != candidate.ContentHash) throw new IOException(L.M("repair.changed"));
            return DesktopAssociation.Preview(current, candidate.ConfigPath);
        }
    }

    public sealed class DesktopSyncResult
    {
        public string Code;
        public string Upstream = "not-run";
        public int ExpectedModels;
        public bool Succeeded { get { return Code == "verified"; } }
    }
    public static class DesktopSync
    {
        public static HashSet<string> Selected(string text)
        {
            var root = JsonData.Parse(text);
            var providers = JsonData.Object(JsonData.Value(root, "providers"));
            if (providers == null && !root.ContainsKey("providers")) return new HashSet<string>(StringComparer.Ordinal);
            if (providers == null) throw new InvalidOperationException(L.M("repair.provider-invalid"));
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in providers.Where(x => x.Key != "openai"))
            {
                var provider = JsonData.Object(pair.Value);
                if (provider == null) throw new InvalidOperationException(L.M("repair.provider-invalid"));
                if (Object.Equals(JsonData.Value(provider, "disabled"), true)) continue;
                var value = JsonData.Value(provider, provider.ContainsKey("selectedModels") ? "selectedModels" : "models");
                if (value != null && !(value is System.Collections.IList)) throw new InvalidOperationException(L.M("repair.provider-invalid"));
                foreach (var model in JsonData.Array(value))
                {
                    var id = model as string; if (id == null) throw new InvalidOperationException(L.M("repair.provider-invalid"));
                    id = id.StartsWith(pair.Key + "/", StringComparison.Ordinal) ? id : pair.Key + "/" + id;
                    ModelNames.ValidateId(id); selected.Add(id);
                }
            }
            return selected;
        }
        public static DesktopSyncResult Verify(PathSet paths, Func<string, string> read)
        {
            var result = new DesktopSyncResult();
            try
            {
                HashSet<string> expected;
                try { expected = Selected(read(paths.OcxConfig)); result.ExpectedModels = expected.Count; }
                catch { result.Code = "provider-invalid"; return result; }
                bool supported; var text = read(paths.CodexConfig);
                if (text == null) { result.Code = "config-missing"; return result; }
                var keys = DesktopDiagnostics.RootKeys(text, out supported);
                if (!supported) { result.Code = "config-unsupported"; return result; }
                string reference; keys.TryGetValue("model_catalog_json", out reference);
                if (expected.Count > 0)
                {
                    if (String.IsNullOrWhiteSpace(reference)) { result.Code = "catalog-reference-missing"; return result; }
                    if (!Path.IsPathRooted(reference)) { result.Code = "catalog-reference-relative"; return result; }
                    var catalog = read(reference);
                    if (catalog == null) { result.Code = "catalog-missing"; return result; }
                    HashSet<string> actual;
                    try
                    {
                        if (!(JsonData.Value(JsonData.Parse(catalog), "models") is System.Collections.IList)) throw new IOException();
                        actual = new HashSet<string>(CatalogReader.ParseCatalog(catalog, false).Select(x => x.Id), StringComparer.Ordinal);
                    }
                    catch { result.Code = "catalog-invalid"; return result; }
                    if (!expected.IsSubsetOf(actual)) { result.Code = "models-missing"; return result; }
                }
                string route; keys.TryGetValue("openai_base_url", out route); Uri uri;
                if (!Uri.TryCreate(route, UriKind.Absolute, out uri) || uri.Scheme != "http" || !uri.IsLoopback)
                { result.Code = "route-missing"; return result; }
                var documents = new List<string>();
                foreach (var name in new[] { "runtime-port.json", "runtime.json" })
                { try { documents.Add(read(Path.Combine(Path.GetDirectoryName(paths.OcxConfig), name))); } catch { } }
                documents.Add(read(paths.OcxConfig));
                if (!OpenCodexEndpointResolver.CandidatesFromDocuments(documents).Contains(uri.Port))
                { result.Code = "route-port-mismatch"; return result; }
                result.Code = "verified"; return result;
            }
            catch { result.Code = "read-failed"; return result; }
        }
        public static string Classify(string output)
        {
            output = output ?? "";
            if (output.IndexOf("no Codex catalog source found", StringComparison.OrdinalIgnoreCase) >= 0) return "no-catalog-source";
            if (output.IndexOf("catalog sync skipped:", StringComparison.OrdinalIgnoreCase) >= 0) return "catalog-skipped";
            if (output.IndexOf("sync skipped", StringComparison.OrdinalIgnoreCase) >= 0) return "sync-skipped";
            return "completed";
        }
        public static async Task<DesktopSyncResult> RunAsync(PathSet paths, Func<CancellationToken, Task<CommandResult>> command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var providerBefore = DesktopDiagnosticInputs.ReadLocal(paths.OcxConfig);
            Selected(providerBefore); // Fail before invoking a mutating CLI on malformed configuration.
            CommandResult response;
            try { response = await command(token); }
            catch (OperationCanceledException) { throw; }
            catch { return new DesktopSyncResult { Code = "command-failed", Upstream = "failed" }; }
            token.ThrowIfCancellationRequested();
            var outcome = Verify(paths, DesktopDiagnosticInputs.ReadLocal);
            outcome.Upstream = Classify(response.Output + "\n" + response.Error);
            if (!response.Succeeded) { outcome.Code = "command-failed"; if (outcome.Upstream == "completed") outcome.Upstream = "failed"; }
            else
            {
                try { if (!Selected(providerBefore).SetEquals(Selected(DesktopDiagnosticInputs.ReadLocal(paths.OcxConfig)))) outcome.Code = "selection-changed"; }
                catch { outcome.Code = "provider-invalid"; }
            }
            return outcome;
        }
    }
}
