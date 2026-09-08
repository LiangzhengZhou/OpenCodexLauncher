using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OpenCodexLauncherV2
{
    public sealed class ModelOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Provider { get; set; }
        public bool IsNative { get; set; }
        public bool IsRouted { get; set; }
        public bool IsAvailable { get; set; }
        public override string ToString() { return DisplayName + "  ·  " + Id; }
    }

    public sealed class ProviderModelChoice : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        private bool _selected;
        public bool Selected { get { return _selected; } set { _selected = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Selected")); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class PathSet
    {
        public string Ocx { get; set; }
        public string Codex { get; set; }
        public string CodexHome { get; set; }
        public string OcxConfig { get; set; }
        public string CodexConfig { get; set; }
        public string Catalog { get; set; }
    }

    public sealed class LauncherSettings
    {
        public string OcxPath { get; set; }
        public string PreviousOcxPath { get; set; }
        public string CodexPath { get; set; }
        public string WorkingDirectory { get; set; }
        public string Language { get; set; }
        public int SettingsVersion { get; set; }
        public bool SetupCompleted { get; set; }
        public string ConfigurationMode { get; set; }
        public string LaunchStrategy { get; set; }
        public string PreferredRuntime { get; set; }
        public string LastSelectedModel { get; set; }
        public bool StrictRouteVerification { get; set; }
        public bool ReserveForceEnabled { get; set; }
        public string ReserveForceTargetModel { get; set; }
        public string ReserveForceTargetRoute { get; set; }
        public bool ReserveForceOwned { get; set; }
        public bool ReserveForceHadPreviousRedirect { get; set; }
        public string ReserveForcePreviousTargetModel { get; set; }
        public string ReserveForceArmedAtUtc { get; set; }
    }

    public enum LaunchStrategy { Force, Auto, FollowCodex }
    public enum RuntimeKind { CodexCli, ClaudeCode, CodexDesktop, Direct }

    public sealed class LauncherModel
    {
        public string Id { get; set; }
        public string RouteId { get; set; }
        public string ProviderId { get; set; }
        public string ProviderDisplayName { get; set; }
        public string ModelId { get; set; }
        public string DisplayName { get; set; }
        public bool Routed { get; set; }
        public bool NativeCodex { get; set; }
        public string Availability { get; set; }
        public string UnavailableReason { get; set; }
    }

    public sealed class LaunchRequest
    {
        public FallbackBudget FallbackBudget { get; set; }
        public string ProjectPath { get; set; }
        public LauncherModel SelectedModel { get; set; }
        public LaunchStrategy Strategy { get; set; }
        public RuntimeKind PreferredRuntime { get; set; }
        public string ReasoningEffort { get; set; }
        public string ResumeSessionId { get; set; }
    }

    public sealed class RuntimeCapability
    {
        public RuntimeKind Runtime { get; set; }
        public bool Available { get; set; }
        public string RoutedModelsSupported { get; set; }
        public string Reason { get; set; }
    }

    public sealed class RuntimeCandidate
    {
        public RuntimeKind Runtime { get; set; }
        public string ModelRouteId { get; set; }
    }

    public sealed class RuntimePlan
    {
        public LauncherModel Model { get; set; }
        public List<RuntimeCandidate> Candidates { get; set; }
        public bool AllowRuntimeFallback { get; set; }
        public bool AllowModelFallback { get; set; }
    }

    public sealed class RunningSession
    {
        // Only set when a runtime supplies an authoritative upstream session ID.
        // A launcher-generated GUID is not evidence of request ownership.
        public string RequestSessionId { get; set; }
        public string Id { get; set; }
        public string ProjectPath { get; set; }
        public string ModelRouteId { get; set; }
        public RuntimeKind Runtime { get; set; }
        public DateTime StartedAt { get; set; }
        public bool Verified { get; set; }
        public bool VerificationStopped { get; set; }
        public Process Process { get; set; }
    }

    public sealed class RouteObservation
    {
        public string SessionId { get; set; }
        public string RequestedModel { get; set; }
        public string ResolvedModel { get; set; }
        public string Provider { get; set; }
        public string RouteReason { get; set; }
        public int? StatusCode { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    public class LauncherError : Exception { public LauncherError(string message) : base(message) { } }
    public sealed class OpenCodexUnavailableError : LauncherError { public OpenCodexUnavailableError(string m) : base(m) { } }
    public sealed class ModelUnavailableError : LauncherError { public ModelUnavailableError(string m) : base(m) { } }
    public sealed class ProviderUnavailableError : LauncherError { public ProviderUnavailableError(string m) : base(m) { } }
    public sealed class RuntimeUnavailableError : LauncherError { public RuntimeUnavailableError(string m) : base(m) { } }
    public sealed class RuntimeModelRejectedError : LauncherError { public RuntimeModelRejectedError(string m) : base(m) { } }
    public sealed class ModelInvariantViolation : LauncherError { public ModelInvariantViolation(string m) : base(m) { } }
    public sealed class ForceLaunchFailedError : LauncherError { public ForceLaunchFailedError(string m) : base(m) { } }

    public static class RouteVerifier
    {
        public static void AssertModelInvariant(string requestedModel, string actualModel)
        {
            if (String.IsNullOrWhiteSpace(requestedModel) || String.IsNullOrWhiteSpace(actualModel) || !String.Equals(requestedModel, actualModel, StringComparison.Ordinal))
                throw new ModelInvariantViolation(L.M("text.170") + requestedModel + L.M("text.171") + actualModel + L.M("text.172"));
        }
    }

    public sealed class RuntimeResolver
    {
        public RuntimePlan Resolve(LaunchRequest request)
        {
            if (request == null || request.SelectedModel == null || String.IsNullOrWhiteSpace(request.SelectedModel.RouteId)) throw new ModelUnavailableError(L.M("text.173"));
            var route = request.SelectedModel.RouteId;
            var candidates = new List<RuntimeCandidate>();
            if (request.Strategy == LaunchStrategy.FollowCodex) candidates.Add(new RuntimeCandidate { Runtime = RuntimeKind.CodexDesktop, ModelRouteId = route });
            else
            {
                if (request.PreferredRuntime == RuntimeKind.ClaudeCode) candidates.Add(new RuntimeCandidate { Runtime = RuntimeKind.ClaudeCode, ModelRouteId = route });
                else candidates.Add(new RuntimeCandidate { Runtime = RuntimeKind.CodexCli, ModelRouteId = route });
                // A preferred runtime is only a first choice. Force/Auto may
                // move to the other runtime, while preserving the same route.
                if (request.Strategy == LaunchStrategy.Force || request.Strategy == LaunchStrategy.Auto)
                    candidates.Add(new RuntimeCandidate { Runtime = candidates[0].Runtime == RuntimeKind.ClaudeCode ? RuntimeKind.CodexCli : RuntimeKind.ClaudeCode, ModelRouteId = route });
                candidates = candidates.GroupBy(x => x.Runtime).Select(x => x.First()).ToList();
            }
            return new RuntimePlan { Model = request.SelectedModel, Candidates = candidates, AllowRuntimeFallback = request.Strategy != LaunchStrategy.FollowCodex, AllowModelFallback = false };
        }
    }

    public sealed class ProviderOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string BaseUrl { get; set; }
        public string Adapter { get; set; }
        public string DefaultModel { get; set; }
        public bool HasApiKey { get; set; }
        public override string ToString() { return DisplayName + " (" + Id + ")"; }
    }

    public static class JsonData
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 100 }; }
        public static Dictionary<string, object> Object(object value) { return value as Dictionary<string, object>; }
        public static List<object> Array(object value)
        {
            if (value == null) return new List<object>();
            var values = value as IList;
            if (values == null) throw new InvalidDataException(L.M("text.174"));
            return values.Cast<object>().ToList();
        }
        public static object Value(Dictionary<string, object> obj, string key) { object value; return obj != null && obj.TryGetValue(key, out value) ? value : null; }
        public static string Text(Dictionary<string, object> obj, params string[] keys)
        {
            foreach (var key in keys) { var value = Value(obj, key) as string; if (!String.IsNullOrWhiteSpace(value)) return value; }
            return String.Empty;
        }
        public static Dictionary<string, object> Parse(string text)
        {
            var value = Object(Serializer().DeserializeObject(text));
            if (value == null) throw new InvalidDataException(L.M("text.175"));
            return value;
        }
        public static Dictionary<string, object> Read(string path) { return File.Exists(path) ? Parse(TextFile.Read(path)) : new Dictionary<string, object>(); }
    }

    public static class ModelNames
    {
        public static void ValidateId(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || id.Length > 256 || !Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._:/+@-]*\z"))
                throw new ArgumentException(L.M("text.176"));
        }
        public static void ValidateProvider(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || !Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z"))
                throw new ArgumentException(L.M("text.177"));
        }
        public static string Slug(string provider, string id) { ValidateProvider(provider); ValidateId(id); return provider + "/" + id.Replace('/', '-'); }
        public static string Pretty(string id)
        {
            if (Regex.IsMatch(id, @"\Agpt-\d", RegexOptions.IgnoreCase))
            {
                var parts = id.Substring(4).Split('-');
                return "GPT-" + parts[0] + (parts.Length > 1 ? " " + String.Join(" ", parts.Skip(1).Select(x => x.Length == 0 ? x : Char.ToUpperInvariant(x[0]) + x.Substring(1))) : "");
            }
            return id.Replace('/', ' ');
        }
        public static string Display(string providerId, string providerName, string id)
        {
            var prefix = String.IsNullOrWhiteSpace(providerName) ? providerId.ToUpperInvariant() : providerName.Trim();
            return (prefix + " " + Pretty(id)).Replace('/', ' ');
        }
    }

    public static class Redactor
    {
        private const string Mask = "***REDACTED***";
        private static bool SecretField(string key)
        {
            var normalized = Regex.Replace(key, "[^A-Za-z]", "").ToLowerInvariant();
            return normalized.Contains("apikey") || normalized.Contains("token") || normalized.Contains("password") || normalized.Contains("secret") || normalized == "authorization" || normalized.Contains("cookie");
        }
        private static object Clean(object value)
        {
            var obj = JsonData.Object(value);
            if (obj != null) return obj.ToDictionary(x => x.Key, x => SecretField(x.Key) ? (object)Mask : Clean(x.Value));
            var array = value as IList;
            if (array != null) return array.Cast<object>().Select(Clean).ToArray();
            return value is string ? CleanText((string)value) : value;
        }
        private static string CleanText(string text)
        {
            text = Regex.Replace(text, @"(?i)sk-[A-Za-z0-9_-]{12,}", Mask);
            text = Regex.Replace(text, @"(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]+", "$1" + Mask);
            text = Regex.Replace(text, @"(?i)([""']?(?:api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|password|secret|cookie)[""']?\s*[=:]\s*)[""']?[^\s,;""'}&]+[""']?", "$1" + Mask);
            return text;
        }
        public static string Apply(string text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            try { return JsonData.Serializer().Serialize(Clean(JsonData.Serializer().DeserializeObject(text))); }
            catch { return CleanText(text); }
        }
    }

    public static class TextFile
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        public static string Read(string path)
        {
            using (var reader = new StreamReader(path, StrictUtf8, true)) return reader.ReadToEnd();
        }
        public static Encoding Detect(string path, string unused)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) return new UTF8Encoding(true);
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) return Encoding.BigEndianUnicode;
            return StrictUtf8;
        }
        public static void AtomicWrite(string path, string text, Encoding encoding)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, text, encoding);
                var writtenHash = FileTransaction.Hash(temporary);
                FileTransaction.BeforeWrite(path);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                FileTransaction.AfterWrite(path, writtenHash);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static class CredentialStore
    {
        public static string PathFor(string id)
        {
            ModelNames.ValidateProvider(id);
            return Path.Combine(LocalEnvironment.Current.DataDirectory, "credentials", id.ToLowerInvariant() + ".bin");
        }
        public static string EnvName(string id)
        {
            ModelNames.ValidateProvider(id);
            using (var sha = SHA256.Create())
                return "OPENCODEX_PROVIDER_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "").Substring(0, 24) + "_API_KEY";
        }
        private static string LegacyEnvName(string id) { return "OPENCODEX_" + Regex.Replace(id.ToUpperInvariant(), "[^A-Z0-9]", "_") + "_API_KEY"; }
        public static byte[] Snapshot(string id) { var path = PathFor(id); return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        public static void Restore(string id, byte[] previous)
        {
            var path = PathFor(id);
            if (previous == null) { if (File.Exists(path)) File.Delete(path); } else File.WriteAllBytes(path, previous);
        }
        public static void Save(string id, string key)
        {
            var path = PathFor(id); Directory.CreateDirectory(Path.GetDirectoryName(path));
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes("OpenCodexLauncher"), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, bytes);
        }
        public static string Load(string id)
        {
            var path = PathFor(id);
            if (File.Exists(path)) return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.UTF8.GetBytes("OpenCodexLauncher"), DataProtectionScope.CurrentUser));
            return LocalEnvironment.Current.Variable(EnvName(id)) ?? LocalEnvironment.Current.Variable(LegacyEnvName(id)) ?? String.Empty;
        }
        public static bool Exists(string id) { try { return !String.IsNullOrWhiteSpace(Load(id)); } catch { return false; } }
        public static string ForProvider(string path, string id)
        {
            var local = Load(id);
            if (!String.IsNullOrWhiteSpace(local)) return local;
            var providers = JsonData.Object(JsonData.Value(JsonData.Read(path), "providers"));
            var provider = JsonData.Object(JsonData.Value(providers, id));
            var configured = JsonData.Text(provider, "apiKey");
            var match = Regex.Match(configured, @"\A\$\{([A-Za-z_][A-Za-z0-9_]*)\}\z");
            return match.Success ? LocalEnvironment.Current.Variable(match.Groups[1].Value) ?? String.Empty : configured;
        }
        public static void ApplyTo(ProcessStartInfo info, string configPath)
        {
            var providers = JsonData.Object(JsonData.Value(JsonData.Read(configPath), "providers"));
            if (providers == null) return;
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in providers)
            {
                var reference = JsonData.Text(JsonData.Object(item.Value), "apiKey");
                var match = Regex.Match(reference, @"\A\$\{([A-Za-z_][A-Za-z0-9_]*)\}\z");
                if (!match.Success) continue;
                var name = match.Groups[1].Value;
                if (owners.ContainsKey(name)) throw new InvalidOperationException(L.M("text.178"));
                owners[name] = item.Key;
                var key = Load(item.Key);
                if (!String.IsNullOrEmpty(key)) info.EnvironmentVariables[name] = key;
            }
        }
    }

    public static class PathResolver
    {
        private static string Existing(string path) { return !String.IsNullOrWhiteSpace(path) && File.Exists(path) ? Path.GetFullPath(path) : null; }
        public static string FromPath(string name)
        {
            foreach (var item in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            { try { var path = Existing(Path.Combine(item.Trim().Trim('"'), name)); if (path != null) return path; } catch (ArgumentException) { } }
            return null;
        }
        private static string Find(string root, string name)
        {
            try { return Directory.Exists(root) ? Directory.GetFiles(root, name, SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (IOException) { return null; }
        }
        private static string Home(string variable, string fallback)
        {
            var path = LocalEnvironment.Current.Variable(variable);
            if (String.IsNullOrWhiteSpace(path)) return fallback;
            if (path == "~") path = LocalEnvironment.Current.UserDirectory;
            else if (path.StartsWith("~/") || path.StartsWith("~\\")) path = Path.Combine(LocalEnvironment.Current.UserDirectory, path.Substring(2));
            return Path.GetFullPath(path);
        }
        public static PathSet Resolve(LauncherSettings settings)
        {
            settings = settings ?? new LauncherSettings();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var profile = LocalEnvironment.Current.UserDirectory;
            var manual = settings.ConfigurationMode == "manual";
            var codexHome = manual ? Path.Combine(LocalEnvironment.Current.DataDirectory, "manual", "codex") : Home("CODEX_HOME", Path.Combine(profile, ".codex"));
            var ocxHome = manual ? Path.Combine(LocalEnvironment.Current.DataDirectory, "manual", "opencodex") : Home("OPENCODEX_HOME", Path.Combine(profile, ".opencodex"));
            return new PathSet {
                Ocx = Existing(settings.OcxPath) ?? (LocalEnvironment.Current.IsIsolated || manual ? null : FromPath("ocx.cmd") ?? Find(Path.Combine(local, "Programs"), "ocx.cmd")),
                Codex = Existing(settings.CodexPath) ?? (LocalEnvironment.Current.IsIsolated || manual ? null : Find(Path.Combine(local, "OpenAI", "Codex", "bin"), "codex.exe") ?? FromPath("codex.exe")),
                CodexHome = codexHome, OcxConfig = Path.Combine(ocxHome, "config.json"), CodexConfig = Path.Combine(codexHome, "config.toml"), Catalog = Path.Combine(codexHome, "opencodex-catalog.json")
            };
        }
        public static PathSet Empty() { return new PathSet(); }
        public static string SettingsPath() { return Path.Combine(LocalEnvironment.Current.DataDirectory, "settings.json"); }
        public static LauncherSettings Load()
        {
            var path = SettingsPath();
            var existing = File.Exists(path);
            var settings = existing ? JsonData.Serializer().Deserialize<LauncherSettings>(TextFile.Read(path)) : new LauncherSettings();
            if (settings == null) throw new InvalidDataException(L.M("settings.invalid"));
            return SetupService.Normalize(settings, existing);
        }
        public static void Save(LauncherSettings settings) { TextFile.AtomicWrite(SettingsPath(), JsonData.Serializer().Serialize(settings), new UTF8Encoding(false)); }
    }

    public static class OpenCodexCompatibility
    {
        const string Marker = "OpenCodex Launcher reserve pre-route patch.";
        public static string FindRouter(string ocxPath)
        {
            var candidates = new List<string>();
            if (!String.IsNullOrWhiteSpace(ocxPath))
            {
                var full = Path.GetFullPath(ocxPath); var dir = Path.GetDirectoryName(full); var name = Path.GetFileName(full);
                if (String.Equals(Path.GetExtension(full), ".mjs", StringComparison.OrdinalIgnoreCase) && String.Equals(name, "ocx.mjs", StringComparison.OrdinalIgnoreCase)) candidates.Add(Path.Combine(dir, "..", "src", "router.ts"));
                if (dir != null)
                {
                    candidates.Add(Path.Combine(dir, "node_modules", "@bitkyc08", "opencodex", "src", "router.ts"));
                    candidates.Add(Path.GetFullPath(Path.Combine(dir, "..", "@bitkyc08", "opencodex", "src", "router.ts")));
                }
            }
            return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        }
        public static string EnsureReservePreRouting(string ocxPath)
        {
            var router = FindRouter(ocxPath);
            if (router == null) throw new InvalidOperationException(L.M("compat.missing"));
            var before = TextFile.Read(router); var lf = before.Replace("\r\n", "\n");
            if (lf.Contains(Marker)) return L.M("compat.ready");
            var anchor = "  const route = routeModelInternal(config, modelId, false, policyEvidence);";
            var compactAnchor = "  const route = routeModelInternal(config, modelId, false, policyEvidence, true);";
            if (lf.IndexOf(anchor, StringComparison.Ordinal) < 0 || lf.IndexOf(compactAnchor, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException(L.M("compat.unsupported"));
            var replacement = "  // " + Marker + "\n"
                + "  const reserveConfig = (config as OcxConfig & { reserveForce?: { enabled?: boolean; targetRoute?: string } }).reserveForce;\n"
                + "  const reserveRewrittenModel = reserveConfig?.enabled === true && modelId === \"gpt-reserve\" && typeof reserveConfig.targetRoute === \"string\" && reserveConfig.targetRoute.includes(\"/\")\n"
                + "    ? reserveConfig.targetRoute\n"
                + "    : modelId;\n"
                + "  const route = routeModelInternal(config, reserveRewrittenModel, false, policyEvidence);\n"
                + "  if (reserveRewrittenModel !== modelId) route.routeReason = \"reserve-force-pre-route\";";
            var compactReplacement = "  // " + Marker + "\n"
                + "  const reserveConfig = (config as OcxConfig & { reserveForce?: { enabled?: boolean; targetRoute?: string } }).reserveForce;\n"
                + "  const reserveRewrittenModel = reserveConfig?.enabled === true && modelId === \"gpt-reserve\" && typeof reserveConfig.targetRoute === \"string\" && reserveConfig.targetRoute.includes(\"/\")\n"
                + "    ? reserveConfig.targetRoute\n"
                + "    : modelId;\n"
                + "  const route = routeModelInternal(config, reserveRewrittenModel, false, policyEvidence, true);\n"
                + "  if (reserveRewrittenModel !== modelId) route.routeReason = \"reserve-force-pre-route\";";
            var afterLf = lf.Replace(anchor, replacement).Replace(compactAnchor, compactReplacement);
            var backup = router + ".launcher-bak." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.Copy(router, backup, false);
            var newline = before.Contains("\r\n") ? "\r\n" : "\n";
            TextFile.AtomicWrite(router, newline == "\r\n" ? afterLf.Replace("\n", "\r\n") : afterLf, TextFile.Detect(router, before));
            return L.M("text.179") + backup;
        }
    }
    public sealed class CommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public bool Cancelled { get; set; }
        public bool TimedOut { get; set; }
        public bool Succeeded { get { return ExitCode == 0 && !Cancelled && !TimedOut; } }
    }

    public sealed class CommandSpec
    {
        public string File { get; set; }
        public string Arguments { get; set; }
        public string Directory { get; set; }
    }

    public static class Commands
    {
        // Windows CRT argument quoting; never pass the result through cmd.exe.
        public static string Quote(string value)
        {
            value = value ?? String.Empty;
            if (value.IndexOf('\0') >= 0) throw new ArgumentException(L.M("text.180"));
            var result = new StringBuilder("\""); var slashes = 0;
            foreach (var c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
                else result.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        public static CommandSpec Ocx(string ocx, params string[] args)
        {
            if (String.IsNullOrWhiteSpace(ocx) || !File.Exists(ocx)) throw new FileNotFoundException(L.M("text.181"));
            var dir = Path.GetDirectoryName(ocx);
            if (Path.GetExtension(ocx).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                return new CommandSpec { File = ocx, Arguments = String.Join(" ", args.Select(Quote)), Directory = dir };
            string script = null;
            if (Path.GetExtension(ocx).Equals(".mjs", StringComparison.OrdinalIgnoreCase)) script = ocx;
            else
            {
                var candidates = new [] { Path.Combine(dir, "node_modules", "@bitkyc08", "opencodex", "bin", "ocx.mjs"), Path.GetFullPath(Path.Combine(dir, "..", "@bitkyc08", "opencodex", "bin", "ocx.mjs")) };
                script = candidates.FirstOrDefault(System.IO.File.Exists);
            }
            if (script == null) throw new InvalidOperationException(L.M("text.182"));
            var node = OpenCodexInstaller.ManagedNode(script) ?? Path.Combine(dir, "node.exe");
            if (!System.IO.File.Exists(node)) node = PathResolver.FromPath("node.exe");
            if (node == null) throw new FileNotFoundException(L.M("text.183"));
            return new CommandSpec { File = node, Arguments = String.Join(" ", new [] { script }.Concat(args).Select(Quote)), Directory = Path.GetDirectoryName(script) };
        }
    }

    public sealed class AsyncProcessRunner
    {
        private static async Task<string> ReadAsync(StreamReader reader, Action<string> onLine)
        {
            var output = new StringBuilder(); string line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (output.Length + line.Length > 8 * 1024 * 1024) throw new InvalidDataException(L.M("text.184"));
                output.AppendLine(line); if (onLine != null) onLine(line);
            }
            return output.ToString();
        }
        public async Task<CommandResult> RunAsync(string file, string args, string cwd, TimeSpan timeout, CancellationToken token, Action<string> onLine, string credentialsConfig = null, string codexHome = null)
        {
            var result = new CommandResult { ExitCode = -1, Output = "", Error = "" };
            token.ThrowIfCancellationRequested();
            using (var process = new Process())
            {
                var info = new ProcessStartInfo(file, args) { WorkingDirectory = cwd ?? Environment.CurrentDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                ApplyHomes(info, credentialsConfig, codexHome);
                process.StartInfo = info; process.EnableRaisingEvents = true;
                var exited = new TaskCompletionSource<int>();
                process.Exited += delegate { exited.TrySetResult(process.ExitCode); };
                process.Start();
                var stdout = ReadAsync(process.StandardOutput, onLine); var stderr = ReadAsync(process.StandardError, onLine);
                var complete = Task.WhenAll(exited.Task, stdout, stderr);
                // A read failure must also terminate a child still producing output.
                var readFailed = new TaskCompletionSource<bool>();
                Task stdoutFailure = stdout.ContinueWith(t => readFailed.TrySetResult(true), TaskContinuationOptions.OnlyOnFaulted);
                Task stderrFailure = stderr.ContinueWith(t => readFailed.TrySetResult(true), TaskContinuationOptions.OnlyOnFaulted);
                using (var delayCancel = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    var deadline = Task.Delay(timeout, delayCancel.Token);
                    var winner = await Task.WhenAny(complete, deadline, readFailed.Task).ConfigureAwait(false);
                    if (winner != complete)
                    {
                        result.Cancelled = token.IsCancellationRequested;
                        result.TimedOut = winner == deadline && !result.Cancelled;
                        KillTree(process.Id);
                        await Task.WhenAny(complete, Task.Delay(3000)).ConfigureAwait(false);
                    }
                    delayCancel.Cancel();
                    if (complete.IsCompleted)
                    {
                        try { await complete.ConfigureAwait(false); result.ExitCode = exited.Task.Result; }
                        catch (Exception ex) { result.Error = Redactor.Apply(ex.Message); }
                    }
                    if (stdout.Status == TaskStatus.RanToCompletion) result.Output = stdout.Result;
                    if (stderr.Status == TaskStatus.RanToCompletion) result.Error += stderr.Result;
                    if (result.TimedOut) result.Error += L.M("text.185");
                    return result;
                }
            }
        }
        public static void KillTree(int id)
        {
            try { using (var process = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + id + " /T /F") { UseShellExecute = false, CreateNoWindow = true })) { if (process != null) process.WaitForExit(5000); } } catch { }
        }
        public static void ApplyHomes(ProcessStartInfo info, string configPath, string codexHome)
        {
            SetupService.PrepareHome(codexHome, "CODEX_HOME");
            if (configPath != null) SetupService.PrepareHome(Path.GetDirectoryName(configPath), "OPENCODEX_HOME");
            if (configPath != null) { CredentialStore.ApplyTo(info, configPath); info.EnvironmentVariables["OPENCODEX_HOME"] = Path.GetDirectoryName(configPath); }
            if (codexHome != null) info.EnvironmentVariables["CODEX_HOME"] = codexHome;
        }
        public static Process StartVisible(string file, string args, string cwd, PathSet paths = null)
        { var info = new ProcessStartInfo(file, args) { WorkingDirectory = cwd, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Normal }; if (paths != null) ApplyHomes(info, paths.OcxConfig, paths.CodexHome); return Process.Start(info); }
        public static Process StartDetached(CommandSpec spec, string credentialsConfig, string codexHome = null)
        {
            var info = new ProcessStartInfo(spec.File, spec.Arguments) { WorkingDirectory = spec.Directory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            ApplyHomes(info, credentialsConfig, codexHome);
            return Process.Start(info);
        }
        public async Task CheckStartupAsync(CommandSpec command, string configPath, string codexHome, CancellationToken token)
        {
            var result = await RunAsync(command.File, command.Arguments, command.Directory,
                TimeSpan.FromSeconds(30), token, null, configPath, codexHome);
            token.ThrowIfCancellationRequested();
            if (result.Succeeded) return;
            var detail = Redactor.Apply(result.Error + "\n" + result.Output).Trim();
            if (detail.Length > 4000) detail = detail.Substring(0, 4000) + "…";
            throw new InvalidOperationException(L.F("startup.preflight", result.ExitCode) + "\n" + detail);
        }
    }

    public sealed class ConfigStore
    {
        private static string Backup(string path) { return path + ".bak." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); }
        private static string MutexName(string path)
        {
            using (var sha = SHA256.Create()) return "Local\\OpenCodexLauncher-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
        }
        private static T Locked<T>(string path, Func<T> action)
        {
            using (var mutex = new Mutex(false, MutexName(path)))
            {
                bool owns;
                try { owns = mutex.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { owns = true; }
                if (!owns) throw new IOException(L.M("text.186"));
                try { return action(); } finally { mutex.ReleaseMutex(); }
            }
        }
        private static string WriteChecked(string path, string before, string after, Encoding encoding)
        {
            var current = File.Exists(path) ? TextFile.Read(path) : null;
            if (!String.Equals(before, current, StringComparison.Ordinal)) throw new IOException(L.M("text.187"));
            var backup = before == null ? L.M("text.188") : Backup(path);
            if (before != null) File.Copy(path, backup, false);
            TextFile.AtomicWrite(path, after, encoding);
            return backup;
        }
        private Task<string> UpdateAsync(string path, Action<Dictionary<string, object>> edit)
        {
            return Task.Run(() => Locked(path, delegate {
                var before = File.Exists(path) ? TextFile.Read(path) : null;
                var root = before == null ? new Dictionary<string, object>() : JsonData.Parse(before);
                edit(root);
                return WriteChecked(path, before, JsonData.Serializer().Serialize(root), new UTF8Encoding(false));
            }));
        }
        public Dictionary<string, object> ReadOcx(string path) { return JsonData.Read(path); }
        public string Provider(string path) { var value = JsonData.Text(ReadOcx(path), "defaultProvider"); return value == "" ? L.M("text.189") : value; }
        public bool AutoStart(string path) { var value = JsonData.Value(ReadOcx(path), "codexAutoStart"); return value is bool && (bool)value; }
        public string ManagementUrl(string path)
        {
            var value = JsonData.Value(ReadOcx(path), "port"); int port;
            if (!Int32.TryParse(Convert.ToString(value), out port) || port < 1 || port > 65535) port = 10100;
            return "http://127.0.0.1:" + port;
        }
        public string CurrentModel(string path)
        {
            if (!File.Exists(path)) return "";
            var text = Regex.Split(TextFile.Read(path), @"(?m)^\s*\[")[0];
            var match = Regex.Match(text, @"(?m)^\s*model\s*=\s*['""]([^'""\r\n]+)['""]");
            return match.Success ? match.Groups[1].Value : "";
        }
        public Task<string> SetDefaultModelAsync(string path, string model)
        {
            ModelNames.ValidateId(model);
            return Task.Run(() => Locked(path, delegate {
                var before = File.Exists(path) ? TextFile.Read(path) : null;
                var text = before ?? "";
                var match = Regex.Match(text, @"(?ms)\A.*?(?=^\s*\[|\z)");
                var root = match.Value; var rest = text.Substring(match.Length);
                var line = "model = \"" + model + "\"";
                root = Regex.IsMatch(root, @"(?m)^\s*model\s*=") ? Regex.Replace(root, @"(?m)^\s*model\s*=[^\r\n]*", m => line) : line + Environment.NewLine + root;
                return WriteChecked(path, before, root + rest, before == null ? new UTF8Encoding(false) : TextFile.Detect(path, before));
            }));
        }
        public Task<string> SetCodexIntegrationAsync(string path, bool enabled)
        {
            return UpdateAsync(path, root => {
                var integrations = JsonData.Object(JsonData.Value(root, "clientIntegrations")) ?? new Dictionary<string, object>();
                integrations["codex"] = enabled;
                root["clientIntegrations"] = integrations;
            });
        }
        public Task<string> PrepareProxyRouteAsync(string path)
        {
            return Task.Run(() => Locked(path, delegate {
                var before = File.Exists(path) ? TextFile.Read(path) : "";
                var firstTable = Regex.Match(before, @"(?m)^\s*\[");
                var rootEnd = firstTable.Success ? firstTable.Index : before.Length;
                var root = before.Substring(0, rootEnd);
                var rest = before.Substring(rootEnd);
                // OpenCodex owns the root route. Remove an old direct/custom route so
                // the upstream injector can install model_provider = "opencodex".
                root = Regex.Replace(root, @"(?m)^\s*model_provider\s*=.*(?:\r?\n|$)", "");
                var after = root + rest;
                if (String.Equals(before, after, StringComparison.Ordinal)) return L.M("text.190");
                return WriteChecked(path, before, after, TextFile.Detect(path, before));
            }));
        }
        public Task<string> SetAutoStartAsync(string path, bool enabled) { return UpdateAsync(path, root => root["codexAutoStart"] = enabled); }
        public string ReserveForceTargetRoute(string path)
        {
            var reserve = JsonData.Object(JsonData.Value(ReadOcx(path), "reserveForce"));
            var enabled = JsonData.Value(reserve, "enabled");
            return enabled is bool && (bool)enabled ? JsonData.Text(reserve, "targetRoute") : "";
        }
        public Task<string> SetReserveForceAsync(string path, string targetRoute, bool enabled)
        {
            if (enabled)
            {
                ModelNames.ValidateId(targetRoute);
                if (!targetRoute.Contains("/")) throw new ArgumentException(L.M("text.191"));
            }
            return UpdateAsync(path, root => {
                if (!enabled) { root.Remove("reserveForce"); return; }
                root["reserveForce"] = new Dictionary<string, object> { { "enabled", true }, { "targetRoute", targetRoute } };
            });
        }
        public string BlockedModelRedirect(string path, string sourceModel)
        {
            ModelNames.ValidateId(sourceModel);
            var redirects = JsonData.Object(JsonData.Value(ReadOcx(path), "blockedModelRedirects"));
            return redirects == null ? "" : JsonData.Text(redirects, sourceModel);
        }
        public Task<string> SetBlockedModelRedirectAsync(string path, string sourceModel, string targetModel, bool enabled)
        {
            ModelNames.ValidateId(sourceModel);
            if (enabled) ModelNames.ValidateId(targetModel);
            return UpdateAsync(path, root => {
                var redirects = JsonData.Object(JsonData.Value(root, "blockedModelRedirects")) ?? new Dictionary<string, object>();
                if (enabled) redirects[sourceModel] = targetModel; else redirects.Remove(sourceModel);
                if (redirects.Count == 0) root.Remove("blockedModelRedirects"); else root["blockedModelRedirects"] = redirects;
            });
        }
        public List<ProviderOption> Providers(string path)
        {
            var providers = JsonData.Object(JsonData.Value(ReadOcx(path), "providers"));
            if (providers == null) return new List<ProviderOption>();
            return providers.Select(item => {
                var p = JsonData.Object(item.Value); var display = JsonData.Text(p, "displayName");
                return new ProviderOption { Id = item.Key, DisplayName = display == "" ? item.Key.ToUpperInvariant() : display, BaseUrl = JsonData.Text(p, "baseUrl"), Adapter = JsonData.Text(p, "adapter"), DefaultModel = JsonData.Text(p, "defaultModel"), HasApiKey = CredentialStore.Exists(item.Key) || !String.IsNullOrEmpty(JsonData.Text(p, "apiKey")) };
            }).OrderBy(x => x.Id).ToList();
        }
        public Task<string> UpsertProviderAsync(string path, ProviderOption provider, string key)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            ModelNames.ValidateProvider(provider.Id);
            if (provider.Id.Equals("openai", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.M("text.192"));
            provider.BaseUrl = ProviderClient.NormalizeBaseUrl(provider.BaseUrl);
            if (provider.Adapter != "openai-chat" && provider.Adapter != "openai-responses") throw new ArgumentException(L.M("text.193"));
            if (!String.IsNullOrEmpty(provider.DefaultModel)) ModelNames.ValidateId(provider.DefaultModel);
            if ((provider.DisplayName ?? "").Length > 100 || (provider.DisplayName ?? "").Any(Char.IsControl)) throw new ArgumentException(L.M("text.194"));
            return Task.Run(() => Locked(path, delegate {
                var before = File.Exists(path) ? TextFile.Read(path) : null;
                var root = before == null ? new Dictionary<string, object>() : JsonData.Parse(before);
                var providers = JsonData.Object(JsonData.Value(root, "providers")) ?? new Dictionary<string, object>();
                if (providers.Keys.Any(id => id != provider.Id && id.Equals(provider.Id, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(L.M("text.195"));
                if (providers.Keys.Any(x => x != provider.Id && String.Equals(x, provider.Id, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Provider IDs cannot differ only by letter case.");
                var old = JsonData.Object(JsonData.Value(providers, provider.Id));
                var p = old == null ? new Dictionary<string, object>() : new Dictionary<string, object>(old);
                p["baseUrl"] = provider.BaseUrl; p["adapter"] = provider.Adapter;
                p["displayName"] = String.IsNullOrWhiteSpace(provider.DisplayName) ? provider.Id.ToUpperInvariant() : provider.DisplayName.Trim();
                if (String.IsNullOrWhiteSpace(provider.DefaultModel)) p.Remove("defaultModel"); else p["defaultModel"] = provider.DefaultModel;
                // New providers remain off until the user chooses models.
                if (old == null) p["disabled"] = true;
                byte[] previousKey = null; bool changedKey = !String.IsNullOrWhiteSpace(key);
                if (changedKey) { previousKey = CredentialStore.Snapshot(provider.Id); CredentialStore.Save(provider.Id, key); p["apiKey"] = "${" + CredentialStore.EnvName(provider.Id) + "}"; }
                try
                {
                    providers[provider.Id] = p; root["providers"] = providers;
                    return WriteChecked(path, before, JsonData.Serializer().Serialize(root), new UTF8Encoding(false));
                }
                catch { if (changedKey) CredentialStore.Restore(provider.Id, previousKey); throw; }
            }));
        }
        public HashSet<string> SelectedModels(string path, string provider)
        {
            var root = ReadOcx(path); var p = JsonData.Object(JsonData.Value(JsonData.Object(JsonData.Value(root, "providers")), provider));
            if (JsonData.Value(p, "disabled") is bool && (bool)p["disabled"]) return new HashSet<string>(StringComparer.Ordinal);
            var chosen = JsonData.Array(JsonData.Value(p, "selectedModels")).OfType<string>().ToList();
            if (chosen.Count == 0) chosen = JsonData.Array(JsonData.Value(p, "models")).OfType<string>()
                .Concat(JsonData.Array(JsonData.Value(p, "retainModels")).OfType<string>())
                .Concat(JsonData.Array(JsonData.Value(root, "customModels")).Select(JsonData.Object).Where(x => JsonData.Text(x, "provider") == provider).Select(x => JsonData.Text(x, "modelId"))).ToList();
            return new HashSet<string>(chosen, StringComparer.Ordinal);
        }
        public Task<string> SelectProviderModelsAsync(string path, string providerId, string displayName, IEnumerable<string> selectedIds, IEnumerable<string> discoveredIds)
        {
            ModelNames.ValidateProvider(providerId);
            if (providerId.Equals("openai", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.M("text.196"));
            var selected = selectedIds.Distinct(StringComparer.Ordinal).ToList();
            var discovered = discoveredIds.Distinct(StringComparer.Ordinal).ToList();
            foreach (var id in selected.Concat(discovered)) ModelNames.ValidateId(id);
            return UpdateAsync(path, root => {
                var providers = JsonData.Object(JsonData.Value(root, "providers"));
                var p = JsonData.Object(JsonData.Value(providers, providerId));
                if (p == null) throw new InvalidOperationException(L.M("text.197"));
                var list = JsonData.Array(JsonData.Value(root, "customModels"));
                var known = list.Select(JsonData.Object).Where(x => JsonData.Text(x, "provider") == providerId).Select(x => JsonData.Text(x, "modelId")).Concat(discovered).Concat(selected).Distinct(StringComparer.Ordinal).ToList();
                if (known.GroupBy(x => x.Replace('/', '-'), StringComparer.Ordinal).Any(x => x.Count() > 1)) throw new InvalidOperationException(L.M("text.198"));
                foreach (var id in selected)
                {
                    var existing = list.Select(JsonData.Object).FirstOrDefault(x => JsonData.Text(x, "provider") == providerId && JsonData.Text(x, "modelId") == id);
                    if (existing == null) { existing = new Dictionary<string, object> { { "id", Guid.NewGuid().ToString() }, { "provider", providerId }, { "modelId", id }, { "addedAt", DateTime.UtcNow.ToString("o") } }; list.Add(existing); }
                    existing["displayName"] = ModelNames.Display(providerId, displayName, id);
                }
                // Persist both the allowlist and the discovered universe. This makes
                // unchecked rows survive a restart and lets the UI show the real saved state.
                p["selectedModels"] = selected.ToArray(); p["models"] = selected.ToArray(); p["liveModels"] = false; p["disabled"] = selected.Count == 0;
                p["discoveredModels"] = discovered.ToArray();
                if (p.ContainsKey("initialModelSelection"))
                {
                    var state = JsonData.Object(p["initialModelSelection"]);
                    if (state != null) state["status"] = selected.Count == 0 ? "all-off" : "ready";
                }
                var labels = JsonData.Object(JsonData.Value(p, "modelDisplayNames")) ?? new Dictionary<string, object>();
                foreach (var id in selected) labels[id] = ModelNames.Display(providerId, displayName, id);
                p["modelDisplayNames"] = labels;
                var disabled = JsonData.Array(JsonData.Value(root, "disabledModels")).OfType<string>().ToList();
                disabled.RemoveAll(x => selected.Any(id => x == providerId + "/" + id || x == ModelNames.Slug(providerId, id)));
                root["disabledModels"] = disabled.ToArray(); root["customModels"] = list.ToArray();
            });
        }
        public async Task AddCustomModelsAsync(string path, string provider, IEnumerable<string> ids)
        {
            var chosen = SelectedModels(path, provider).Concat(ids).Distinct(StringComparer.Ordinal).ToArray();
            var p = Providers(path).FirstOrDefault(x => x.Id == provider);
            await SelectProviderModelsAsync(path, provider, p == null ? provider : p.DisplayName, chosen, chosen).ConfigureAwait(false);
        }
        public Task<string> SetDefaultProviderAsync(string path, string id)
        {
            return UpdateAsync(path, root => {
                var p = JsonData.Object(JsonData.Value(JsonData.Object(JsonData.Value(root, "providers")), id));
                if (p == null || (JsonData.Value(p, "disabled") is bool && (bool)p["disabled"])) throw new InvalidOperationException(L.M("text.199"));
                root["defaultProvider"] = id;
            });
        }
    }

    public static class ProviderClient
    {
        public static string NormalizeBaseUrl(string input)
        {
            Uri uri;
            if (!Uri.TryCreate((input ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment)) throw new ArgumentException(L.M("text.200"));
            if (uri.Scheme == "http" && !uri.IsLoopback) throw new ArgumentException(L.M("text.201"));
            var result = uri.AbsoluteUri.TrimEnd('/');
            foreach (var suffix in new [] { "/chat/completions", "/responses", "/models" }) if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { result = result.Substring(0, result.Length - suffix.Length); break; }
            return result.TrimEnd('/');
        }
        public static List<string> ParseModels(string text)
        {
            var root = JsonData.Parse(text); var raw = JsonData.Value(root, "data");
            if (raw == null) throw new InvalidDataException(L.M("text.202"));
            var result = new List<string>();
            foreach (var item in JsonData.Array(raw))
            {
                var id = JsonData.Text(JsonData.Object(item), "id");
                if (id == "") throw new InvalidDataException(L.M("text.203"));
                ModelNames.ValidateId(id); result.Add(id);
                if (result.Count > 10000) throw new InvalidDataException(L.M("text.204"));
            }
            return result.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        public static async Task<List<string>> FetchModelsAsync(ProviderOption provider, string key, CancellationToken token)
        {
            var baseUrl = NormalizeBaseUrl(provider.BaseUrl);
            var urls = new List<string> { baseUrl + "/models" };
            if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) urls.Add(baseUrl + "/v1/models");
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 8 * 1024 * 1024 })
            {
                for (var i = 0; i < urls.Count; i++)
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, urls[i]))
                    {
                        if (!String.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                        using (var response = await client.SendAsync(request, token).ConfigureAwait(false))
                        {
                            if (response.StatusCode == HttpStatusCode.NotFound && i + 1 < urls.Count) continue;
                            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(L.M("text.205") + (int)response.StatusCode + (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden ? L.M("text.206") : L.M("text.207")));
                            return ParseModels(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        }
                    }
                }
            }
            throw new InvalidOperationException(L.M("text.208"));
        }
    }

    public static class CatalogReader
    {
        public static List<ModelOption> ParseCatalog(string json, bool nativeOnly)
        {
            var root = JsonData.Parse(json); var models = new List<ModelOption>();
            foreach (var raw in JsonData.Array(JsonData.Value(root, "models")))
            {
                var item = JsonData.Object(raw); var id = JsonData.Text(item, "slug", "id");
                if (id == "" || (nativeOnly && id.Contains('/'))) continue;
                if (JsonData.Text(item, "visibility") == "hide") continue;
                ModelNames.ValidateId(id);
                var native = !id.Contains('/');
                var label = JsonData.Text(item, "display_name", "displayName", "name");
                models.Add(new ModelOption { Id = id, DisplayName = label == "" ? ModelNames.Pretty(id) : label, Provider = native ? "openai" : id.Split('/')[0], IsNative = native, IsRouted = !native, IsAvailable = false });
            }
            return models;
        }
        public static List<ModelOption> Load(PathSet paths, ConfigStore store) { return Load(paths, store, new List<ModelOption>()); }
        public static List<ModelOption> Load(PathSet paths, ConfigStore store, IEnumerable<ModelOption> nativeModels, Action<string> warning = null, IList<ModelOption> catalogCache = null)
        {
            // The provider config is authoritative. A generated Codex catalog is optional
            // and may be stale or temporarily unreadable while the runtime rewrites it.
            var root = store.ReadOcx(paths.OcxConfig);
            var map = new Dictionary<string, ModelOption>(StringComparer.Ordinal);
            var catalog = new List<ModelOption>();
            try
            {
                if (File.Exists(paths.Catalog)) catalog = ParseCatalog(TextFile.Read(paths.Catalog), false);
                if (catalogCache != null) { catalogCache.Clear(); foreach (var model in catalog) catalogCache.Add(model); }
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is ArgumentException) && !(error is InvalidOperationException)) throw;
                if (catalogCache != null) catalog.AddRange(catalogCache);
                if (warning != null) warning("catalog-unreadable");
            }
            foreach (var model in catalog) map[model.Id] = model;
            foreach (var model in nativeModels) map[model.Id] = model;
            var providers = JsonData.Object(JsonData.Value(root, "providers"));
            if (providers != null) foreach (var entry in providers)
            {
                if (entry.Key == "openai") continue;
                var p = JsonData.Object(entry.Value);
                var ids = JsonData.Array(JsonData.Value(p, "selectedModels")).OfType<string>()
                    .Concat(JsonData.Array(JsonData.Value(p, "models")).OfType<string>())
                    .Concat(JsonData.Array(JsonData.Value(p, "retainModels")).OfType<string>()).Distinct(StringComparer.Ordinal);
                foreach (var id in ids)
                {
                    var slug = ModelNames.Slug(entry.Key, id);
                    var label = JsonData.Text(JsonData.Object(JsonData.Value(p, "modelDisplayNames")), id);
                    map[slug] = new ModelOption { Id = slug, DisplayName = label == "" ? ModelNames.Display(entry.Key, JsonData.Text(p, "displayName"), id) : label, Provider = entry.Key, IsRouted = true };
                }
            }
            foreach (var raw in JsonData.Array(JsonData.Value(root, "customModels")))
            {
                var cm = JsonData.Object(raw); var provider = JsonData.Text(cm, "provider"); var id = JsonData.Text(cm, "modelId");
                if (provider == "" || id == "" || provider == "openai") continue;
                var p = JsonData.Object(JsonData.Value(providers, provider));
                if (p == null) continue;
                var slug = ModelNames.Slug(provider, id);
                map[slug] = new ModelOption { Id = slug, DisplayName = JsonData.Text(cm, "displayName") == "" ? ModelNames.Display(provider, JsonData.Text(p, "displayName"), id) : JsonData.Text(cm, "displayName"), Provider = provider, IsRouted = true };
            }
            var disabled = new HashSet<string>(JsonData.Array(JsonData.Value(root, "disabledModels")).OfType<string>(), StringComparer.Ordinal);
            foreach (var model in map.Values.ToList())
            {
                if (model.IsNative) continue;
                var p = JsonData.Object(JsonData.Value(providers, model.Provider));
                if (p == null) continue; // Preserve account-bound or combo entries from the authoritative catalog.
                var selected = JsonData.Array(JsonData.Value(p, "selectedModels")).OfType<string>().Select(x => ModelNames.Slug(model.Provider, x)).ToList();
                if ((JsonData.Value(p, "disabled") is bool && (bool)p["disabled"]) || (selected.Count > 0 && !selected.Contains(model.Id)) || disabled.Contains(model.Id)) map.Remove(model.Id);
            }
            return map.Values.OrderByDescending(x => x.IsNative).ThenBy(x => x.Id == "gpt-6-astra" ? "" : x.DisplayName).ToList();
        }
    }

    public sealed class OpenCodexEndpoint
    {
        public string BaseUrl { get; set; }
        public int Port { get; set; }
        public string ApiUrl { get { return BaseUrl.TrimEnd('/') + "/api"; } }
        public string DataUrl { get { return BaseUrl.TrimEnd('/') + "/v1"; } }
    }

    public static class OpenCodexEndpointResolver
    {
        static int Port(object value)
        {
            int port; return Int32.TryParse(Convert.ToString(value), out port) && port > 0 && port <= 65535 ? port : 0;
        }
        public static List<int> Candidates(string configPath)
        {
            var result = new List<int>();
            try
            {
                var home = Path.GetDirectoryName(configPath);
                foreach (var file in new [] { Path.Combine(home, "runtime-port.json"), Path.Combine(home, "runtime.json") })
                {
                    if (!File.Exists(file)) continue;
                    try {
                        var root = JsonData.Read(file);
                        var p = Port(JsonData.Value(root, "port")); if (p > 0 && !result.Contains(p)) result.Add(p);
                    } catch (ArgumentException) { } catch (InvalidDataException) { } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                var configured = Port(JsonData.Value(JsonData.Read(configPath), "port")); if (configured > 0 && !result.Contains(configured)) result.Add(configured);
            }
            catch { }
            if (result.Count == 0) result.Add(10100);
            return result;
        }
        public static async Task<OpenCodexEndpoint> ResolveAsync(string configPath, CancellationToken token)
        {
            foreach (var port in Candidates(configPath))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) })
                    using (var response = await client.GetAsync("http://127.0.0.1:" + port + "/healthz", token).ConfigureAwait(false))
                    if (response.IsSuccessStatusCode) return new OpenCodexEndpoint { BaseUrl = "http://127.0.0.1:" + port, Port = port };
                }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); }
                catch (HttpRequestException) { }
            }
            throw new OpenCodexUnavailableError(L.M("text.209"));
        }
    }

    public sealed class OpenCodexClient
    {
        readonly string configPath;
        readonly Func<CancellationToken, Task<OpenCodexEndpoint>> endpoint;
        public OpenCodexClient(string configPath) : this(configPath, t => OpenCodexEndpointResolver.ResolveAsync(configPath, t)) { }
        public OpenCodexClient(string configPath, Func<CancellationToken, Task<OpenCodexEndpoint>> resolver) { this.configPath = configPath; endpoint = resolver; }
        string AdminToken()
        {
            var path = Path.Combine(Path.GetDirectoryName(configPath), "admin-api-token");
            try { return File.Exists(path) ? TextFile.Read(path).Trim() : ""; } catch { return ""; }
        }
        async Task<Dictionary<string, object>> GetObjectAsync(string path, CancellationToken token)
        {
            var e = await endpoint(token).ConfigureAwait(false);
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            using (var request = new HttpRequestMessage(HttpMethod.Get, e.ApiUrl + path))
            {
                var tokenValue = AdminToken(); if (!String.IsNullOrWhiteSpace(tokenValue)) request.Headers.Add("x-opencodex-api-key", tokenValue);
                using (var response = await client.SendAsync(request, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new OpenCodexUnavailableError(L.M("text.210") + (int)response.StatusCode + "。");
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try { return JsonData.Parse(text); }
                    catch (InvalidDataException)
                    {
                        var array = JsonData.Serializer().DeserializeObject(text) as IList;
                        if (array != null) return new Dictionary<string, object> { { "data", array } };
                        throw;
                    }
                }
            }
        }
        public async Task<bool> GetHealthAsync(CancellationToken token) { try { await endpoint(token).ConfigureAwait(false); return true; } catch { return false; } }
        public Task<Dictionary<string, object>> GetProvidersAsync(CancellationToken token) { return GetObjectAsync("/providers", token); }
        public Task<Dictionary<string, object>> GetProviderQuotasAsync(CancellationToken token) { return GetObjectAsync("/provider-quotas", token); }
        public Task<Dictionary<string, object>> GetCodexQuotaAsync(CancellationToken token) { return GetObjectAsync("/codex-auth/quota", token); }
        public Task<Dictionary<string, object>> GetLogsAsync(CancellationToken token) { return GetObjectAsync("/logs", token); }
        public Task<Dictionary<string, object>> GetModelsAsync(CancellationToken token) { return GetObjectAsync("/models", token); }
        public async Task<RouteObservation> GetLatestObservationAsync(CancellationToken token, DateTime? since = null)
        {
            var root = await GetLogsAsync(token).ConfigureAwait(false);
            var rows = JsonData.Array(JsonData.Value(root, "logs")).Select(JsonData.Object).Where(x => x != null).ToList();
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                var stampUtc = DateTime.MinValue;
                try
                {
                    var rawValue = JsonData.Value(rows[i], "timestamp");
                    var rawText = rawValue as string;
                    if (!String.IsNullOrWhiteSpace(rawText)) { DateTime parsed; if (DateTime.TryParse(rawText, null, System.Globalization.DateTimeStyles.RoundtripKind, out parsed)) stampUtc = parsed.ToUniversalTime(); }
                    else { var raw = Convert.ToDouble(rawValue); if (raw > 100000000000) raw /= 1000.0; if (raw > 0) stampUtc = DateTimeOffset.FromUnixTimeSeconds((long)raw).UtcDateTime; }
                    if (since.HasValue && stampUtc != DateTime.MinValue && stampUtc < since.Value) continue;
                }
                catch { }
                var requested = JsonData.Text(rows[i], "requestedModel", "model");
                var resolved = JsonData.Text(rows[i], "resolvedModel", "model");
                if (String.IsNullOrWhiteSpace(requested) && String.IsNullOrWhiteSpace(resolved)) continue;
                var decision = JsonData.Object(JsonData.Value(rows[i], "routeDecision"));
                int statusCode; var statusValue = JsonData.Value(rows[i], "status") ?? JsonData.Value(rows[i], "statusCode"); int? status = Int32.TryParse(Convert.ToString(statusValue), out statusCode) ? (int?)statusCode : null;
                return new RouteObservation { RequestedModel = requested, ResolvedModel = resolved, Provider = JsonData.Text(rows[i], "provider"), RouteReason = decision == null ? JsonData.Text(rows[i], "routeReason") : JsonData.Text(decision, "routeReason"), StatusCode = status, TimestampUtc = stampUtc };
            }
            return null;
        }
        public async Task<string> GetLatestRouteAsync(CancellationToken token, DateTime? since = null)
        {
            var observation = await GetLatestObservationAsync(token, since).ConfigureAwait(false);
            return observation == null ? "" : (String.IsNullOrWhiteSpace(observation.ResolvedModel) ? observation.RequestedModel : observation.ResolvedModel);
        }
        public async Task<RouteObservation> GetSessionObservationAsync(RunningSession session, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(session.RequestSessionId)) return null;
            return SessionEvidence.Find(await GetLogsAsync(token).ConfigureAwait(false), session);
        }
        public async Task<List<string>> GetGatewayModelsAsync(CancellationToken token)
        {
            var e = await endpoint(token).ConfigureAwait(false);
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            using (var response = await client.GetAsync(e.DataUrl + "/models?limit=1000", token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) throw new OpenCodexUnavailableError(L.M("text.211") + (int)response.StatusCode + "。");
                return ProviderClient.ParseModels(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }
    }

    public sealed class ClaudeModelResolver
    {
        readonly OpenCodexClient client;
        public ClaudeModelResolver(OpenCodexClient client) { this.client = client; }
        public async Task<string> ResolveAsync(string routeId, CancellationToken token)
        {
            var models = await client.GetGatewayModelsAsync(token).ConfigureAwait(false);
            // Prefer an exact route exposed by the gateway. Alias generation remains an
            // OpenCodex concern; the launcher never reconstructs its alias algorithm.
            var exact = models.FirstOrDefault(x => String.Equals(x, routeId, StringComparison.Ordinal));
            if (exact != null) return exact;
            throw new ModelUnavailableError(L.M("alias.missing") + routeId);
        }
    }

    public static class RuntimeCapabilityProbe
    {
        public static RuntimeCapability Probe(RuntimeKind kind, PathSet paths)
        {
            if (kind == RuntimeKind.CodexCli) return new RuntimeCapability { Runtime = kind, Available = !String.IsNullOrWhiteSpace(paths.Codex) && File.Exists(paths.Codex), RoutedModelsSupported = "unknown", Reason = paths.Codex == null ? L.M("text.212") : "" };
            if (kind == RuntimeKind.ClaudeCode)
            {
                var claude = PathResolver.FromPath("claude.exe") ?? PathResolver.FromPath("claude.cmd") ?? PathResolver.FromPath("claude");
                return new RuntimeCapability { Runtime = kind, Available = claude != null, RoutedModelsSupported = "unknown", Reason = claude == null ? L.M("text.213") : "" };
            }
            return new RuntimeCapability { Runtime = kind, Available = kind == RuntimeKind.CodexDesktop, RoutedModelsSupported = "unknown" };
        }
    }

    public sealed class RuntimeController
    {
        readonly PathSet paths; readonly OpenCodexClient client; readonly CancellationToken token;
        public RuntimeController(PathSet paths, OpenCodexClient client, CancellationToken token) { this.paths = paths; this.client = client; this.token = token; }
        public async Task<RunningSession> LaunchAsync(LaunchRequest request)
        {
            var plan = new RuntimeResolver().Resolve(request); var errors = new List<string>();
            var budget = request.FallbackBudget ?? new FallbackBudget();
            var attempt = 0;
            if (!await client.GetHealthAsync(token).ConfigureAwait(false)) throw new OpenCodexUnavailableError(L.M("text.214"));
            if (plan.Model.Routed)
            {
                try
                {
                    var management = await client.GetModelsAsync(token).ConfigureAwait(false);
                    var rows = JsonData.Array(JsonData.Value(management, "data")).Select(JsonData.Object).Where(x => x != null).ToList();
                    var exists = rows.Any(x => String.Equals(JsonData.Text(x, "namespaced", "route", "id"), plan.Model.RouteId, StringComparison.Ordinal) && !(JsonData.Value(x, "disabled") is bool && (bool)x["disabled"]));
                    if (!exists)
                    {
                        var gateway = await client.GetGatewayModelsAsync(token).ConfigureAwait(false);
                        exists = gateway.Any(x => String.Equals(x, plan.Model.RouteId, StringComparison.Ordinal));
                    }
                    if (!exists) throw new ModelUnavailableError(L.M("text.215") + plan.Model.RouteId + "。");
                }
                catch (OpenCodexUnavailableError) { throw; }
                catch (Exception ex) { throw new ModelUnavailableError(L.M("text.216") + Redactor.Apply(ex.Message)); }
            }
            foreach (var candidate in plan.Candidates)
            {
                token.ThrowIfCancellationRequested();
                if (attempt++ > 0 && !budget.TryUse(true)) break;
                if (candidate.ModelRouteId != plan.Model.RouteId) throw new ModelInvariantViolation(L.M("text.217"));
                Process started = null;
                try
                {
                    var capability = RuntimeCapabilityProbe.Probe(candidate.Runtime, paths);
                    if (!capability.Available && candidate.Runtime != RuntimeKind.ClaudeCode) throw new RuntimeUnavailableError(capability.Reason ?? L.M("text.218"));
                    Process process; string runtimeModel = candidate.ModelRouteId; var startedAt = DateTime.UtcNow;
                    if (candidate.Runtime == RuntimeKind.CodexCli)
                    {
                        var cwd = Directory.Exists(request.ProjectPath) ? request.ProjectPath : Environment.CurrentDirectory;
                        process = AsyncProcessRunner.StartVisible(paths.Codex, "-m " + Commands.Quote(candidate.ModelRouteId), cwd, paths); started = process;
                    }
                    else if (candidate.Runtime == RuntimeKind.ClaudeCode)
                    {
                        var alias = await new ClaudeModelResolver(client).ResolveAsync(candidate.ModelRouteId, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        var command = Commands.Ocx(paths.Ocx, "claude", "--model", alias);
                        process = AsyncProcessRunner.StartVisible(command.File, command.Arguments, request.ProjectPath, paths); started = process;
                        runtimeModel = candidate.ModelRouteId;
                    }
                    else throw new RuntimeUnavailableError(L.M("text.219"));
                    RouteVerifier.AssertModelInvariant(candidate.ModelRouteId, runtimeModel);
                    return new RunningSession { Id = Guid.NewGuid().ToString("N"), ProjectPath = request.ProjectPath, ModelRouteId = candidate.ModelRouteId, Runtime = candidate.Runtime, StartedAt = startedAt, Verified = false, Process = process };
                }
                catch (Exception ex) { if (started != null) { try { if (!started.HasExited) AsyncProcessRunner.KillTree(started.Id); } catch { } } errors.Add(candidate.Runtime + ": " + Redactor.Apply(ex.Message)); }
            }
            throw new ForceLaunchFailedError(L.M("text.220") + plan.Model.RouteId + L.M("text.221") + String.Join("、", plan.Candidates.Select(x => x.Runtime)) + L.M("text.222") + String.Join("\n", errors));
        }
        public async Task<bool> VerifySessionRouteAsync(RunningSession session)
        {
            var observation = await client.GetSessionObservationAsync(session, token).ConfigureAwait(false);
            if (observation == null) return false;
            RouteVerifier.AssertModelInvariant(session.ModelRouteId, SessionEvidence.Route(observation, session.ModelRouteId));
            session.Verified = true; return true;
        }
    }
}
