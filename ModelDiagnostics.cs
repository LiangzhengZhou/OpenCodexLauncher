using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace OpenCodexLauncherV2
{
    // Deliberately an allowlist of counts and fixed state codes. Never serialize a
    // config object, exception message, provider/model name, path, URL or credential.
    public static class ModelDiagnostics
    {
        public static string Create(PathSet paths, LauncherSettings settings, IEnumerable<ModelOption> displayed, string refreshState)
        {
            var report = new Dictionary<string, object> {
                { "launcherVersion", Assembly.GetExecutingAssembly().GetName().Version.ToString() },
                { "setupCompleted", settings.SetupCompleted },
                { "desktopConfigAssociated", !String.IsNullOrWhiteSpace(settings.DesktopConfigPath) },
                { "desktopLoaded", "unverified" },
                { "configurationMode", settings.ConfigurationMode == "manual" ? "manual" : "associated" },
                { "nativeRefresh", new [] { "ok", "failed", "not-requested", "not-configured" }.Contains(refreshState) ? refreshState : "unknown" },
                { "codexExecutableExists", File.Exists(paths.Codex) },
                { "codexHomeExists", Directory.Exists(paths.CodexHome) },
                { "codexConfigExists", File.Exists(paths.CodexConfig) },
                { "visibleThirdPartyModels", displayed.Count(x => x.IsRouted) },
                { "visibleNativeModels", displayed.Count(x => x.IsNative) }
            };
            try
            {
                var root = JsonData.Read(paths.OcxConfig);
                var providers = JsonData.Object(JsonData.Value(root, "providers")) ?? new Dictionary<string, object>();
                report["providerConfig"] = File.Exists(paths.OcxConfig) ? "readable" : "missing";
                var rows = providers.Where(x => x.Key != "openai").Select(x => JsonData.Object(x.Value)).ToList();
                report["thirdPartyProviders"] = rows.Count;
                report["disabledProviders"] = rows.Count(p => Object.Equals(JsonData.Value(p, "disabled"), true));
                report["selectedModelEntries"] = rows.Sum(p => JsonData.Array(JsonData.Value(p, "selectedModels")).Count);
                report["configuredModelEntries"] = rows.Sum(p => JsonData.Array(JsonData.Value(p, "models")).Count);
                report["customModelEntries"] = JsonData.Array(JsonData.Value(root, "customModels")).Count;
            }
            catch (Exception) { report["providerConfig"] = "unreadable"; }
            try
            {
                var catalog = File.Exists(paths.Catalog) ? CatalogReader.ParseCatalog(TextFile.Read(paths.Catalog), false) : new List<ModelOption>();
                report["catalog"] = File.Exists(paths.Catalog) ? "readable" : "missing";
                report["catalogThirdPartyModels"] = catalog.Count(x => x.IsRouted);
            }
            catch (Exception) { report["catalog"] = "unreadable"; }
            return JsonData.Serializer().Serialize(report);
        }
    }
}
