using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    // JSON-RPC only. No HTTP client, credentials, prompt logs, or virtual thread IDs.
    public sealed class NativeControlBridge
    {
        readonly object gate = new object();
        readonly Dictionary<string, string> threads = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> pending = new Dictionary<string, string>();
        readonly Dictionary<string, string> diagnosticMethods = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Func<Dictionary<string, NativeRoute>> loadRoutes;
        public NativeControlBridge(Func<Dictionary<string, NativeRoute>> load) { loadRoutes = load; }
        static string Id(Dictionary<string, object> m) { return JsonData.Serializer().Serialize(JsonData.Value(m, "id")); }
        public string Input(string frame, out string error)
        {
            error = null;
            var message = JsonData.Parse(frame); var p = JsonData.Object(JsonData.Value(message, "params"));
            string method = JsonData.Text(message, "method"), model = JsonData.Text(p, "model");
            lock (gate)
            {
                if ((method == "initialize" || method == "configRequirements/read") && diagnosticMethods.Count < 64)
                {
                    diagnosticMethods[Id(message)] = method;
                    NativeBridgeDiagnostics.Method(method);
                }
                // Desktop can override the turn model through collaboration settings.
                // Apply the same attested-thread checks to that explicit protocol field.
                if (method == "turn/start")
                {
                    var collaboration = JsonData.Object(JsonData.Value(p, "collaborationMode"));
                    var settings = JsonData.Object(JsonData.Value(collaboration, "settings"));
                    var nestedModel = JsonData.Text(settings, "model");
                    if (!String.IsNullOrEmpty(nestedModel))
                    {
                        var check = JsonData.Serializer().Serialize(new { id = JsonData.Value(message, "id"), method = "turn/start",
                            @params = new { threadId = JsonData.Text(p, "threadId"), model = nestedModel } });
                        var rewritten = Input(check, out error);
                        if (error != null) return null;
                        var checkedParams = JsonData.Object(JsonData.Value(JsonData.Parse(rewritten), "params"));
                        var checkedModel = JsonData.Text(checkedParams, "model");
                        if (checkedModel != nestedModel)
                        {
                            settings["model"] = checkedModel;
                            frame = JsonData.Serializer().Serialize(message);
                        }
                    }
                }
                string currentProvider;
                if (model != "" && !model.StartsWith("launcher-native-", StringComparison.Ordinal) &&
                    (method == "turn/start" || method == "thread/settings/update") &&
                    threads.TryGetValue(JsonData.Text(p, "threadId"), out currentProvider))
                {
                    var known = loadRoutes().Values.Where(x => x != null && x.Provider == currentProvider).ToArray();
                    if (known.Length > 0 && !known.Any(x => x.Model == model))
                    { error = Error(message, "Changing provider or selecting an unconfigured native model requires a new conversation."); return null; }
                }
                if (model.StartsWith("launcher-native-", StringComparison.Ordinal))
                {
                    NativeRoute target; var routes = loadRoutes();
                    if (!routes.TryGetValue(model, out target) || target == null || target.Mode != ProviderRouteMode.NativeCodex || String.IsNullOrWhiteSpace(target.Provider) || String.IsNullOrWhiteSpace(target.Model))
                    { error = Error(message, "Unknown or unavailable Launcher native model alias."); return null; }
                    if (method == "thread/start") { p["model"] = target.Model; p["modelProvider"] = target.Provider; }
                    else
                    {
                        string bound;
                        if ((method != "turn/start" && method != "thread/settings/update") || !threads.TryGetValue(JsonData.Text(p, "threadId"), out bound) || bound != target.Provider)
                        { error = Error(message, "Changing provider requires a new conversation. No request was sent to another provider."); return null; }
                        p["model"] = target.Model;
                    }
                    frame = JsonData.Serializer().Serialize(message);
                }
                if (method == "thread/start" || method == "thread/resume" || method == "thread/fork" || method == "thread/read")
                {
                    if (pending.Count >= 4096) throw new InvalidDataException("Too many pending thread operations.");
                    pending[Id(message)] = JsonData.Text(p, "modelProvider");
                }
                return frame;
            }
        }
        static string Error(Dictionary<string, object> m, string text)
        { return JsonData.Serializer().Serialize(new { id = JsonData.Value(m, "id"), error = new { code = -32602, message = text } }); }
        public void Output(string frame)
        {
            var message = JsonData.Parse(frame);
            lock (gate)
            {
                var id = Id(message);
                string diagnosticMethod;
                if (diagnosticMethods.TryGetValue(id, out diagnosticMethod))
                {
                    diagnosticMethods.Remove(id);
                    if (diagnosticMethod == "configRequirements/read")
                    {
                        var result = JsonData.Object(JsonData.Value(message, "result")) ?? new Dictionary<string, object>();
                        var requirements = JsonData.Object(JsonData.Value(result, "configRequirements")) ?? new Dictionary<string, object>();
                        // Log schema presence only; never values, IDs or payloads.
                        NativeBridgeDiagnostics.ResponseSchema(requirements.Count > 0,
                            requirements.ContainsKey("requirements"),
                            JsonData.Value(result, "appServerVersion") != null);
                    }
                }
                string requested;
                if (!pending.TryGetValue(id, out requested)) return;
                pending.Remove(id);
                var threadResult = JsonData.Object(JsonData.Value(message, "result"));
                var thread = JsonData.Object(JsonData.Value(threadResult, "thread"));
                string threadId = JsonData.Text(thread, "id"), provider = JsonData.Text(threadResult, "modelProvider");
                if (provider == "") provider = JsonData.Text(thread, "modelProvider");
                // Never assume a requested provider was adopted if upstream did not attest it.
                if (threadId != "" && provider != "") threads[threadId] = provider;
            }
        }
        public static string Quote(string value)
        {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (var c in value) { if (c == '\\') { slashes++; continue; } b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c); slashes = 0; }
            return b.Append('\\', slashes * 2).Append('"').ToString();
        }
        static async Task Frames(Stream source, Func<byte[], Task> accept)
        {
            using (var pending = new MemoryStream())
            {
                byte[] buffer = new byte[32768]; int count;
                while ((count = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    for (int i = 0; i < count; i++)
                    {
                        pending.WriteByte(buffer[i]);
                        if (pending.Length > 16 * 1024 * 1024) throw new InvalidDataException("Control-plane frame too large.");
                        if (buffer[i] == 10) { await accept(pending.ToArray()); pending.SetLength(0); }
                    }
                if (pending.Length != 0) throw new InvalidDataException("Incomplete control-plane frame.");
            }
        }
        public static int Run(string manifest, string[] args)
        {
            return Run(manifest, args, null, null, true);
        }
        // Test seam: production callers use Run(manifest, args) only.
        public static int Run(string manifest, string[] args, string packageRoot, string runtimeRoot)
        {
            return Run(manifest, args, packageRoot, runtimeRoot, true);
        }
        public static int Run(string manifest, string[] args, string packageRoot, string runtimeRoot, bool bridgeInput)
        {
            try { return RunAsync(manifest, args, packageRoot, runtimeRoot, bridgeInput).GetAwaiter().GetResult(); }
            catch (NativeRuntimeException error)
            {
                NativeBridgeDiagnostics.Failure(error.Code, error.GetType().Name, error.HResult);
                Console.Error.WriteLine("Native bridge runtime check failed: " + error.Code);
                return 2;
            }
            catch (Exception error)
            {
                NativeBridgeDiagnostics.Failure("bridge-failure", error.GetType().Name, error.HResult);
                Console.Error.WriteLine("Native bridge startup or control-plane failure.");
                return 2;
            }
        }
        static async Task<int> RunAsync(string manifest, string[] args, string packageRoot, string runtimeRoot, bool bridgeInput)
        {
            var settings = JsonData.Serializer().Deserialize<NativeBridgeSettings>(TextFile.Read(manifest));
            // Resolve here, not in the GUI: daily activation works while Launcher is closed.
            // Keep the manifest read-only to avoid racing Prepare/rollback or external edits.
            var root = String.IsNullOrWhiteSpace(runtimeRoot) ? CodexRuntime.DesktopRoot : runtimeRoot;
            var executable = CodexRuntime.ResolveNative(settings, root, CodexRuntime.FindUsable, packageRoot);
            NativeBridgeDiagnostics.RuntimeSelected(CodexRuntime.RuntimeId(executable), settings.AutomaticRuntime != false);
            var own = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (Path.GetFullPath(executable).Equals(own, StringComparison.OrdinalIgnoreCase))
                throw new NativeRuntimeException("helper-path-invalid", "The Native runtime resolves to the helper itself.");
            var bridge = new NativeControlBridge(() => JsonData.Serializer().Deserialize<Dictionary<string, NativeRoute>>(TextFile.Read(settings.RoutesPath)));
            var start = new ProcessStartInfo(executable, String.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (String.IsNullOrWhiteSpace(settings.CodexHome) || !Directory.Exists(settings.CodexHome))
                throw new NativeRuntimeException("codex-home-missing", "The configured Codex home is missing.");
            start.EnvironmentVariables["CODEX_HOME"] = settings.CodexHome;
            if (!CodexRuntime.IsUsable(executable))
                throw new NativeRuntimeException("runtime-changed", "The Codex runtime changed before launch. Retry after the Desktop update.");
            using (var child = Process.Start(start))
            using (var outputLock = new System.Threading.SemaphoreSlim(1))
            {
                if (child == null) throw new NativeRuntimeException("child-start-failed", "Codex could not be started.");
                NativeBridgeDiagnostics.ChildStarted(child.Id);
                var output = Console.OpenStandardOutput();
                Func<byte[], Task> write = async bytes => { await outputLock.WaitAsync(); try { await output.WriteAsync(bytes, 0, bytes.Length); await output.FlushAsync(); } finally { outputLock.Release(); } };
                var utf8 = new UTF8Encoding(false, true);
                bool rpc = args.Contains("app-server");
                var outgoing = rpc ? Frames(child.StandardOutput.BaseStream, async frame => { bridge.Output(utf8.GetString(frame)); await write(frame); }) : child.StandardOutput.BaseStream.CopyToAsync(output);
                Task incoming;
                if (!rpc) incoming = Console.OpenStandardInput().CopyToAsync(child.StandardInput.BaseStream);
                else if (!bridgeInput) incoming = new TaskCompletionSource<object>().Task;
                else incoming = Frames(Console.OpenStandardInput(), async frame => {
                    string original = utf8.GetString(frame), error;
                    var forwarded = bridge.Input(original, out error);
                    if (error != null) await write(utf8.GetBytes(error + "\n"));
                    else { var bytes = forwarded == original ? frame : utf8.GetBytes(forwarded + "\n"); await child.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length); await child.StandardInput.BaseStream.FlushAsync(); }
                });
                // Count stderr only. Its content can contain upstream diagnostics,
                // prompts or credentials and is never persisted.
                var errors = Drain(child.StandardError.BaseStream);
                var exit = Task.Run(() => child.WaitForExit());
                var first = await Task.WhenAny(exit, incoming, outgoing);
                if (first != exit)
                {
                    if (first.IsFaulted)
                    {
                        NativeBridgeDiagnostics.Failure("control-plane-fault", first.Exception.GetType().Name, first.Exception.HResult);
                        if (!child.HasExited) child.Kill();
                    }
                    else child.StandardInput.Close();
                    if (await Task.WhenAny(exit, Task.Delay(5000)) != exit && !child.HasExited) child.Kill();
                }
                await exit; await outgoing; var stderrBytes = await errors;
                NativeBridgeDiagnostics.ChildExit(child.Id, child.ExitCode, stderrBytes);
                if (incoming.IsFaulted) throw new NativeRuntimeException("control-plane-input-failed", "Control-plane input failed.");
                return child.ExitCode;
            }
        }
        static async Task<long> Drain(Stream stream)
        {
            var buffer = new byte[8192]; long total = 0; int count;
            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0) total += count;
            return total;
        }
    }
}
