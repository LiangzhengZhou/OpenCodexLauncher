using System;
using System.IO;
using System.Linq;
using System.Globalization;

namespace OpenCodexLauncherV2
{
    // A single explicit environment seam. Tests never use the Windows user's stores.
    public sealed class LocalEnvironment
    {
        public string DataDirectory { get; private set; }
        public string UserDirectory { get; private set; }
        public bool IsIsolated { get; private set; }
        public LocalEnvironment(string dataDirectory, string userDirectory, bool isolated)
        { DataDirectory = Path.GetFullPath(dataDirectory); UserDirectory = Path.GetFullPath(userDirectory); IsIsolated = isolated; }
        public static LocalEnvironment Current = new LocalEnvironment(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCodexLauncher"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false);
        public static void UseIsolated(string directory)
        { Current = new LocalEnvironment(Path.Combine(directory, "launcher"), Path.Combine(directory, "user"), true); }
        public string Variable(string name) { return IsIsolated ? null : Environment.GetEnvironmentVariable(name); }
    }
    public static class SetupService
    {
        public const int Version = 1;
        public static LauncherSettings Normalize(LauncherSettings value, bool existing)
        {
            if (value.SettingsVersion > Version) throw new InvalidDataException(L.M("settings.newer"));
            if (existing && value.SettingsVersion == 0)
            {
                value.SetupCompleted = !String.IsNullOrWhiteSpace(value.OcxPath) || !String.IsNullOrWhiteSpace(value.CodexPath) ||
                    !String.IsNullOrWhiteSpace(value.WorkingDirectory) || !String.IsNullOrWhiteSpace(value.LastSelectedModel) ||
                    !String.IsNullOrWhiteSpace(value.LaunchStrategy) || value.ReserveForceEnabled;
            }
            value.SettingsVersion = Version;
            if (String.IsNullOrWhiteSpace(value.Language)) value.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
            if (value.Language != "zh" && value.Language != "en") value.Language = "en";
            if (String.IsNullOrWhiteSpace(value.LaunchStrategy)) value.LaunchStrategy = "follow-codex";
            if (String.IsNullOrWhiteSpace(value.PreferredRuntime)) value.PreferredRuntime = "codex-cli";
            return value;
        }
        public static void Validate(PathSet paths)
        {
            if (File.Exists(paths.OcxConfig)) JsonData.Read(paths.OcxConfig);
            if (File.Exists(paths.Catalog)) CatalogReader.ParseCatalog(TextFile.Read(paths.Catalog), false);
            if (File.Exists(paths.CodexConfig)) TextFile.Read(paths.CodexConfig);
        }
        public static void Prepare(LauncherSettings settings, PathSet paths)
        {
            Validate(paths);
            if (!settings.SetupCompleted || settings.ConfigurationMode != "manual") return;
            PrepareHome(paths.CodexHome, "CODEX_HOME");
            PrepareHome(Path.GetDirectoryName(paths.OcxConfig), "OPENCODEX_HOME");
        }
        public static void PrepareHome(string path, string variable)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path);
            var owned = Path.Combine(LocalEnvironment.Current.DataDirectory, "manual",
                variable == "CODEX_HOME" ? "codex" : "opencodex");
            try
            {
                // Only repair launcher-owned homes. A missing imported/custom home may
                // be a disconnected disk or a typo; never silently replace that choice.
                if (String.Equals(full.TrimEnd(Path.DirectorySeparatorChar), owned, StringComparison.OrdinalIgnoreCase))
                    Directory.CreateDirectory(full);
                if (!Directory.Exists(full)) throw new DirectoryNotFoundException();
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                throw new IOException(L.F("startup.home", variable, full), error);
            }
        }
    }
}
