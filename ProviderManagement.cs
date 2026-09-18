using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    // One editor and model workspace, two storage backends. Native records must
    // never become enabled OpenCodex relay providers.
    public static class ProviderManagement
    {
        public static List<ProviderOption> List(string path)
        {
            var native = NativeProviders.Read();
            var items = new ConfigStore().Providers(path).Where(x => x.Id != "openai").ToList();
            // Earlier self-tests allowed the same ID in both stores. Show it once;
            // the next explicit save resolves that duplicate transactionally.
            items.RemoveAll(x => native.Any(n => n.Provider.Id == x.Id));
            items.AddRange(native.Select(x => x.Provider));
            return items.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
        }
        public static string Key(string path, ProviderOption p)
        {
            return p.RouteMode == ProviderRouteMode.NativeCodex
                ? CredentialStore.Load(NativeProviders.CredentialId(p.Id)) : CredentialStore.ForProvider(path, p.Id);
        }
        public static async Task Save(PathSet paths, ProviderOption provider, string editedKey,
            IEnumerable<string> selected, IEnumerable<string> discovered, bool reserveForce, string helper)
        {
            ModelNames.ValidateProvider(provider.Id);
            if (provider.Id.Equals("openai", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.M("text.192"));
            await Mutate(paths, provider.Id, reserveForce, helper, async () => {
                var config = new ConfigStore(); var native = NativeProviders.Read();
                var previous = native.FirstOrDefault(x => x.Provider.Id == provider.Id);
                var proxy = config.Providers(paths.OcxConfig).FirstOrDefault(x => x.Id == provider.Id);
                var key = !String.IsNullOrWhiteSpace(editedKey) ? editedKey : (previous != null ? Key(paths.OcxConfig, previous.Provider) : proxy != null ? Key(paths.OcxConfig, proxy) : "");
                if (List(paths.OcxConfig).Any(x => x.Id != provider.Id && x.Id.Equals(provider.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Provider IDs cannot differ only by case.");
                if (provider.RouteMode == ProviderRouteMode.NativeCodex)
                {
                    var record = new NativeProviderRecord { Provider = provider, Models = selected.Distinct().ToArray(),
                        DiscoveredModels = discovered.Concat(selected).Distinct().ToArray(),
                        LastFetch = previous == null ? null : previous.LastFetch,
                        ConnectionFingerprint = previous == null ? null : previous.ConnectionFingerprint };
                    NativeProviders.Validate(record);
                    if (proxy != null) await config.RemoveProviderAsync(paths.OcxConfig, provider.Id);
                    native.RemoveAll(x => x.Provider.Id == provider.Id); native.Add(record);
                    if (previous == null || !String.IsNullOrWhiteSpace(editedKey)) CredentialStore.Save(NativeProviders.CredentialId(provider.Id), key);
                    // Empty encrypted tombstone prevents environment fallback after conversion.
                    if (proxy != null) CredentialStore.Save(provider.Id, "");
                }
                else
                {
                    await config.UpsertProviderAsync(paths.OcxConfig, provider, previous == null ? editedKey : key);
                    if (previous != null)
                    {
                        await config.SelectProviderModelsAsync(paths.OcxConfig, provider.Id, provider.DisplayName, selected, discovered);
                        native.Remove(previous); CredentialStore.Save(NativeProviders.CredentialId(provider.Id), "");
                    }
                }
                WriteNative(native);
            }, provider.RouteMode == ProviderRouteMode.NativeCodex);
        }
        public static Task Delete(PathSet paths, string id, bool reserveForce, string helper)
        {
            ModelNames.ValidateProvider(id);
            if (id.Equals("openai", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.M("text.192"));
            return Mutate(paths, id, reserveForce, helper, async () => {
                var config = new ConfigStore();
                if (config.Providers(paths.OcxConfig).Any(x => x.Id == id)) await config.RemoveProviderAsync(paths.OcxConfig, id);
                var native = NativeProviders.Read(); native.RemoveAll(x => x.Provider.Id == id); WriteNative(native);
                CredentialStore.Save(id, ""); CredentialStore.Save(NativeProviders.CredentialId(id), "");
            }, false, true);
        }
        public static Task Select(PathSet paths, ProviderOption p, IEnumerable<string> selected, IEnumerable<string> all, bool reserve, string helper)
        {
            if (p.RouteMode == ProviderRouteMode.NativeCodex) return Save(paths, p, null, selected, all, reserve, helper);
            return new ConfigStore().SelectProviderModelsAsync(paths.OcxConfig, p.Id, p.DisplayName, selected, all);
        }
        public static async Task RecordModels(PathSet paths, ProviderOption p, IEnumerable<string> all, string fingerprint, string time)
        {
            if (p.RouteMode == ProviderRouteMode.OpenCodexProxy) { await new ConfigStore().RecordProviderModelsAsync(paths.OcxConfig, p.Id, all, fingerprint, time); return; }
            await NativeProviders.prepareGate.WaitAsync();
            try {
                var records = NativeProviders.Read(); var record = records.Single(x => x.Provider.Id == p.Id);
                record.DiscoveredModels = all.Distinct().ToArray(); record.LastFetch = time; record.ConnectionFingerprint = fingerprint;
                NativeProviders.Validate(record); WriteNative(records);
            } finally { NativeProviders.prepareGate.Release(); }
        }
        static void WriteNative(List<NativeProviderRecord> records)
        { TextFile.AtomicWrite(NativeProviders.StorePath, JsonData.Serializer().Serialize(records), new UTF8Encoding(false)); }
        static async Task Mutate(PathSet paths, string id, bool reserveForce, string helper, Func<Task> edit, bool toNative, bool deleting = false)
        {
            await NativeProviders.prepareGate.WaitAsync();
            try
            {
                var wasNative = NativeProviders.Read().Any(x => x.Provider.Id == id);
                if (reserveForce && (wasNative || toNative || deleting)) throw new InvalidOperationException(L.M("provider.forceInUse"));
                string catalog = null;
                if (File.Exists(paths.CodexConfig)) {
                    bool supported; var keys = DesktopDiagnostics.RootKeys(TextFile.Read(paths.CodexConfig), out supported);
                    string candidate; if (supported && keys.TryGetValue("model_catalog_json", out candidate) && Path.IsPathRooted(candidate) && File.Exists(candidate)) catalog = candidate;
                }
                var sync = (wasNative || toNative) && File.Exists(NativeProviders.StatePath);
                var transaction = new FileTransaction(new[] { paths.OcxConfig, NativeProviders.StorePath, NativeProviders.RoutesPath,
                    NativeProviders.StatePath, NativeProviders.BridgePath, paths.CodexConfig, paths.Catalog, catalog,
                    CredentialStore.PathFor(id), CredentialStore.PathFor(NativeProviders.CredentialId(id)) });
                try
                {
                    await transaction.Step(async () => {
                        await edit();
                        if (deleting || toNative || wasNative) {
                            // Drop only this provider's old relay slugs; leave official and
                            // other providers' entries untouched. Native aliases use sync state.
                            foreach (var path in new[] { catalog, paths.Catalog }.Where(x => !String.IsNullOrEmpty(x) && File.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase)) {
                                var doc = JsonData.Read(path);
                                if (!doc.ContainsKey("models")) throw new InvalidDataException("Unsupported catalog format.");
                                doc["models"] = JsonData.Array(doc["models"]).Where(x => !JsonData.Text(JsonData.Object(x), "slug").StartsWith(id + "/", StringComparison.Ordinal)).ToArray();
                                TextFile.AtomicWrite(path, JsonData.Serializer().Serialize(doc), new UTF8Encoding(false));
                            }
                        }
                        if (sync) await NativeProviders.PrepareCore(paths, NativeActivation.ResolveHelper(helper), reserveForce, transaction);
                    });
                    transaction.Commit();
                }
                catch { transaction.Rollback(); throw; }
            }
            finally { NativeProviders.prepareGate.Release(); }
        }
    }
}
