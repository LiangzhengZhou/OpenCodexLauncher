using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    public sealed class DesktopProcessEvidence
    {
        public bool Available;
        public int DesktopCandidates, AppServers;
    }

    // Dependencies are explicit so regression checks cannot discover the host's accounts,
    // inspect its processes or contact its proxy. No raw observation enters the report.
    public sealed class DesktopDiagnosticInputs
    {
        public string DefaultHome, EnvironmentHome;
        public Func<string, string> Read;
        public Func<CancellationToken, Task<DesktopProcessEvidence>> Processes;
        public Func<int, CancellationToken, Task<bool>> Health;
        public static DesktopDiagnosticInputs Local()
        {
            var env = LocalEnvironment.Current;
            return new DesktopDiagnosticInputs {
                DefaultHome = Path.Combine(env.UserDirectory, ".codex"), EnvironmentHome = env.Variable("CODEX_HOME"),
                Read = ReadLocal,
                Processes = env.IsIsolated ? (Func<CancellationToken, Task<DesktopProcessEvidence>>)(t => Task.FromResult(new DesktopProcessEvidence())) : ReadProcesses,
                Health = env.IsIsolated ? (Func<int, CancellationToken, Task<bool>>)((p,t) => Task.FromResult(false)) : ProbeHealth
            };
        }
        public static string ReadLocal(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            var full = Path.GetFullPath(path);
            if (new[] { "auth.json", "credentials.json", "settings.json", "admin-api-token" }.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase))
                throw new IOException();
            if (!Path.IsPathRooted(path) || full.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(full)).DriveType != DriveType.Fixed)
                throw new IOException();
            // Avoid following links to network shares or unrelated stores.
            for (var p = full; !String.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException();
            try
            {
                using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    const int limit = 2 * 1024 * 1024;
                    if (stream.Length > limit) throw new IOException();
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        var buffer = new char[limit + 1]; var count = 0;
                        while (count < buffer.Length) { var n = reader.Read(buffer, count, buffer.Length - count); if (n == 0) break; count += n; }
                        if (count > limit) throw new IOException();
                        return new string(buffer, 0, count);
                    }
                }
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }
        public static async Task<bool> ProbeHealth(int port, CancellationToken token)
        {
            if (port < 1 || port > 65535) return false;
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(1) })
            {
                try
                {
                    using (var response = await client.GetAsync("http://127.0.0.1:" + port + "/healthz", HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                        return response.IsSuccessStatusCode;
                }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return false; }
                catch (HttpRequestException) { return false; }
            }
        }
        static Task<DesktopProcessEvidence> ReadProcesses(CancellationToken token)
        {
            return Task.Run(() => {
                var result = new DesktopProcessEvidence();
                try
                {
                    var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(3), ReturnImmediately = false };
                    using (var search = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, CommandLine FROM Win32_Process WHERE Name = 'Codex.exe'", options))
                    using (var rows = search.Get())
                    {
                        int count = 0; bool hidden = false;
                        foreach (ManagementObject row in rows)
                        using (row)
                        {
                            token.ThrowIfCancellationRequested(); if (++count > 256) return new DesktopProcessEvidence();
                            var command = row["CommandLine"] as string;
                            if (command == null) { hidden = true; continue; }
                            if (Regex.IsMatch(command, @"(?:^|\s)app-server(?:\s|$)")) result.AppServers++;
                            else if (!command.Contains("--type=")) result.DesktopCandidates++;
                        }
                        result.Available = !hidden;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { return new DesktopProcessEvidence(); }
                return result;
            }, token);
        }
    }

    public sealed class DesktopDiagnosticReport
    {
        public string Json { get; internal set; }
        public string[] Findings { get; internal set; }
    }

    public static class DesktopDiagnostics
    {
        static bool Same(string a, string b)
        {
            try { return !String.IsNullOrWhiteSpace(a) && !String.IsNullOrWhiteSpace(b) && String.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        // Only root scalar keys are inspected. Nested MCP environment values are never
        // treated as the Desktop's environment. Unsupported syntax stays unknown.
        public static Dictionary<string, string> RootKeys(string text, out bool supported)
        {
            supported = true; var result = new Dictionary<string, string>();
            foreach (var line in (text ?? "").Split('\n'))
            {
                var s = line.Trim(); if (s.StartsWith("[")) break;
                if (s.StartsWith("#")) continue;
                if (s.Contains("\"\"\"") || s.Contains("'''")) { supported = false; break; }
                if (Regex.IsMatch(s, "^[\"'](?:model_catalog_json|openai_base_url|model)[\"']\\s*=")) { supported = false; continue; }
                var key = Regex.Match(s, "^(model_catalog_json|openai_base_url|model)\\s*=");
                if (!key.Success) continue;
                var value = s.Substring(key.Length).Trim(); string parsed = null;
                if (!value.StartsWith("\"\"\"") && !value.StartsWith("'''"))
                {
                    var literal = Regex.Match(value, "^'([^']*)'\\s*(?:#.*)?$");
                    var basic = Regex.Match(value, "^(\"(?:[^\"\\\\]|\\\\.)*\")\\s*(?:#.*)?$");
                    if (literal.Success) parsed = literal.Groups[1].Value;
                    else if (basic.Success) { try { parsed = JsonData.Serializer().Deserialize<string>(basic.Groups[1].Value); } catch { } }
                }
                if (parsed == null || result.ContainsKey(key.Groups[1].Value)) supported = false;
                else result[key.Groups[1].Value] = parsed;
            }
            return result;
        }
        static string Read(DesktopDiagnosticInputs input, string path, out string state)
        {
            try { var text = input.Read(path); state = text == null ? "missing" : "readable"; return text; }
            catch { state = "unreadable-or-unsupported"; return null; }
        }
        static List<ModelOption> Catalog(DesktopDiagnosticInputs input, string path, out string state)
        {
            var text = Read(input, path, out state); if (text == null) return null;
            try { return CatalogReader.ParseCatalog(text, false); }
            catch { state = "invalid"; return null; }
        }
        public static async Task<DesktopDiagnosticReport> CollectAsync(PathSet paths, LauncherSettings settings, DesktopDiagnosticInputs input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!settings.SetupCompleted) return Finish(new Dictionary<string, object> { { "status", "setup-required" } }, new List<string> { "setup-required" });
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var work = Task.Run(() => Collect(paths, settings, input, deadline.Token), deadline.Token);
                var timeout = Task.Delay(TimeSpan.FromSeconds(12), token);
                if (await Task.WhenAny(work, timeout).ConfigureAwait(false) != work)
                {
                    deadline.Cancel();
                    // Observe late failures from blocked OS reads without keeping the UI waiting.
                    ObserveFailure(work);
                    token.ThrowIfCancellationRequested();
                    return Finish(new Dictionary<string, object> { { "status", "timed-out" } }, new List<string> { "timed-out" });
                }
                try { return await work.ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch { return Finish(new Dictionary<string, object> { { "status", "inspection-failed" } }, new List<string> { "inspection-failed" }); }
            }
        }
        static void ObserveFailure(Task task)
        { task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        static async Task<DesktopDiagnosticReport> Collect(PathSet paths, LauncherSettings settings, DesktopDiagnosticInputs input, CancellationToken token)
        {
            var report = new Dictionary<string, object> { { "status", "completed" }, { "desktopLoaded", "unverified" },
                { "desktopHomeEvidence", "candidates-only" }, { "associated", !String.IsNullOrWhiteSpace(settings.DesktopConfigPath) } };
            var findings = new List<string>();
            string state; var providerText = Read(input, paths.OcxConfig, out state);
            var expected = new HashSet<string>(StringComparer.Ordinal);
            bool providersKnown = false;
            if (providerText != null)
            {
                try
                {
                    var root = JsonData.Parse(providerText);
                    var providers = JsonData.Object(JsonData.Value(root, "providers")) ?? new Dictionary<string, object>();
                    int enabled = 0;
                    foreach (var pair in providers.Where(p => p.Key != "openai"))
                    {
                        var p = JsonData.Object(pair.Value); if (Object.Equals(JsonData.Value(p, "disabled"), true)) continue;
                        enabled++;
                        foreach (var id in JsonData.Array(JsonData.Value(p, "selectedModels")).OfType<string>())
                            expected.Add(id.StartsWith(pair.Key + "/", StringComparison.Ordinal) ? id : pair.Key + "/" + id);
                    }
                    report["enabledThirdPartyProviders"] = enabled; providersKnown = true;
                }
                catch { state = "invalid"; }
            }
            report["providerConfig"] = state; report["selectedThirdPartyModels"] = providersKnown ? (object)expected.Count : null;
            if (!providersKnown) findings.Add("provider-unreadable");
            else if (expected.Count == 0) findings.Add("no-selected-models");
            var portDocuments = new List<string>();
            foreach (var name in new[] { "runtime-port.json", "runtime.json" })
            {
                token.ThrowIfCancellationRequested();
                try { var text = Read(input, Path.Combine(Path.GetDirectoryName(paths.OcxConfig), name), out state); if (text != null) portDocuments.Add(text); } catch { }
            }
            portDocuments.Add(providerText);
            var runtimePorts = OpenCodexEndpointResolver.CandidatesFromDocuments(portDocuments);
            var rows = new List<Dictionary<string, object>>();
            var routePorts = new Dictionary<int, int>();
            var homes = new[] { paths.CodexHome, input.DefaultHome, input.EnvironmentHome };
            var roles = new[] { "launcher-target", "default-home", "launcher-environment-home" };
            bool targetHasModels = false, alternativeMissing = false;
            for (int i = 0; i < homes.Length; i++)
            {
                token.ThrowIfCancellationRequested(); if (String.IsNullOrWhiteSpace(homes[i])) continue;
                var row = new Dictionary<string, object> { { "role", roles[i] }, { "sameAsTarget", Same(homes[i], paths.CodexHome) } }; rows.Add(row);
                if (i > 0 && Same(homes[i], paths.CodexHome)) { row["state"] = "same-as-target"; continue; }
                string text;
                try { text = Read(input, i == 0 ? paths.CodexConfig : Path.Combine(homes[i], "config.toml"), out state); }
                catch { text = null; state = "unreadable-or-unsupported"; }
                row["config"] = state; if (text == null) continue;
                string cacheState;
                var cache = Catalog(input, Path.Combine(homes[i], "models_cache.json"), out cacheState);
                row["nativeCache"] = cacheState;
                row["nativeCacheModels"] = cache == null ? null : (object)cache.Count(m => m.IsNative);
                bool supported; var keys = RootKeys(text, out supported);
                row["rootKeys"] = supported ? "inspected" : "unsupported";
                if (!supported) { findings.Add("config-syntax-unknown"); continue; }
                string reference, route;
                keys.TryGetValue("model_catalog_json", out reference); keys.TryGetValue("openai_base_url", out route);
                row["catalogReferencePresent"] = !String.IsNullOrWhiteSpace(reference);
                Uri uri; bool loopback = Uri.TryCreate(route, UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https") && uri.IsLoopback;
                row["route"] = String.IsNullOrWhiteSpace(route) ? "absent" : loopback ? "loopback" : "other";
                row["routePortCandidateMatches"] = loopback && runtimePorts.Contains(uri.Port);
                if (loopback && uri.Scheme == "http") routePorts[rows.Count - 1] = uri.Port;
                if (i == 0 && String.IsNullOrWhiteSpace(route)) findings.Add("target-route-missing");
                if (i == 0 && !String.IsNullOrWhiteSpace(route) && (!loopback || !runtimePorts.Contains(uri.Port))) findings.Add("target-route-differs");
                if (String.IsNullOrWhiteSpace(reference))
                {
                    if (i == 0) findings.Add("target-catalog-reference-missing"); else alternativeMissing = true;
                    continue;
                }
                // Relative references have runtime-dependent semantics: do not guess.
                if (!Path.IsPathRooted(reference)) { row["catalog"] = "relative-reference-unverified"; findings.Add("relative-catalog"); continue; }
                var catalog = Catalog(input, reference, out state); row["catalog"] = state;
                row["catalogMatchesGeneratedFile"] = Same(reference, paths.Catalog);
                if (catalog != null)
                {
                    var ids = new HashSet<string>(catalog.Where(m => m.IsRouted).Select(m => m.Id), StringComparer.Ordinal);
                    row["thirdPartyModels"] = ids.Count;
                    row["selectedModelsMissing"] = providersKnown ? (object)expected.Count(id => !ids.Contains(id)) : null;
                    if (i == 0) { targetHasModels = ids.Count > 0; if (providersKnown && expected.Any(id => !ids.Contains(id))) findings.Add("target-models-missing"); }
                    else if (ids.Count == 0) alternativeMissing = true;
                }
                else if (i == 0) findings.Add("target-catalog-unreadable");
            }
            report["homes"] = rows;
            var generated = Catalog(input, paths.Catalog, out state); report["generatedCatalog"] = state;
            report["generatedThirdPartyModels"] = generated == null ? null : (object)generated.Count(m => m.IsRouted);
            if (expected.Count > 0 && state == "missing") findings.Add("generated-catalog-missing");
            if (targetHasModels && alternativeMissing) findings.Add("possible-home-mismatch");
            if (String.IsNullOrWhiteSpace(settings.DesktopConfigPath)) findings.Add("desktop-home-unconfirmed");
            token.ThrowIfCancellationRequested();
            Task<DesktopProcessEvidence> processTask;
            try { processTask = input.Processes(token); }
            catch { processTask = Task.FromResult(new DesktopProcessEvidence()); }
            int? healthyPort = null;
            foreach (var p in runtimePorts)
            {
                token.ThrowIfCancellationRequested();
                try { if (await input.Health(p, token).ConfigureAwait(false)) { healthyPort = p; break; } }
                catch (OperationCanceledException) { throw; } catch { }
            }
            report["health"] = healthyPort.HasValue ? "loopback-http-success" : "unavailable";
            report["healthScope"] = "healthz-only-no-inference-service-identity-unverified";
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].ContainsKey("route")) rows[i]["routeMatchesHealthyProxy"] = healthyPort.HasValue ? (object)(routePorts.ContainsKey(i) && routePorts[i] == healthyPort.Value) : null;
            if (healthyPort.HasValue && rows.Count > 0 && rows[0].ContainsKey("route") && (!routePorts.ContainsKey(0) || routePorts[0] != healthyPort.Value)) findings.Add("target-route-differs");
            if (!healthyPort.HasValue) findings.Add("proxy-unavailable");
            DesktopProcessEvidence processes;
            try { processes = await processTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; } catch { processes = new DesktopProcessEvidence(); }
            report["processInspection"] = processes.Available ? "available" : "unavailable";
            report["desktopCandidateProcesses"] = processes.Available ? (object)processes.DesktopCandidates : null;
            report["appServerProcesses"] = processes.Available ? (object)processes.AppServers : null;
            if (!processes.Available) findings.Add("processes-unknown");
            else
            {
                if (processes.DesktopCandidates == 0) findings.Add("desktop-not-detected");
                if (processes.AppServers > 0) findings.Add("app-server-active");
            }
            findings.Add("desktop-loading-unverified");
            token.ThrowIfCancellationRequested(); return Finish(report, findings);
        }
        static DesktopDiagnosticReport Finish(Dictionary<string, object> data, List<string> findings)
        {
            data["reportVersion"] = 1; data["launcherVersion"] = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            data["capturedAtUtc"] = DateTime.UtcNow.ToString("o"); data["findings"] = findings.Distinct().ToArray();
            return new DesktopDiagnosticReport { Json = JsonData.Serializer().Serialize(data), Findings = findings.Distinct().ToArray() };
        }
    }
}
