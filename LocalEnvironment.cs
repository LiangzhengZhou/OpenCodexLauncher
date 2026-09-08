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
    }
}
