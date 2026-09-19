using System;
using System.IO;
using System.Linq;

namespace OpenCodexLauncherV2
{
    // Shared by GUI discovery and the independently launched stable Native helper.
    public static class CodexRuntime
    {
        public static string DesktopRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin"); } }
        public static bool IsUsable(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || !File.Exists(executable)) return false;
            return File.Exists(Path.Combine(Path.GetDirectoryName(executable), "codex-code-mode-host.exe"));
        }
        public static bool IsDesktopRuntime(string executable, string root)
        {
            if (String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable)) return false;
            return Path.GetFullPath(executable).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        public static string FindUsable(string root)
        {
            try
            {
                return Directory.Exists(root) ? Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories)
                    .Where(IsUsable).OrderByDescending(File.GetLastWriteTimeUtc)
                    .ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault() : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        public static string ResolveNative(NativeBridgeSettings settings, string root, Func<string, string> discover)
        {
            if (settings == null) throw new InvalidDataException("Native runtime manifest is missing.");
            if (IsUsable(settings.RealCodex)) return settings.RealCodex;
            // Legacy manifests carry no selection provenance. Only recover targets
            // inside Desktop's managed root; never substitute a manual CLI or PATH entry.
            if (settings.AutomaticRuntime == false || !IsDesktopRuntime(settings.RealCodex, root))
                throw new InvalidDataException("Configured Native Codex runtime is incomplete. Select a complete runtime in Launcher.");
            var candidate = discover(root);
            if (!IsDesktopRuntime(candidate, root) || !IsUsable(candidate))
                throw new InvalidDataException("No complete Codex Desktop runtime is available. Finish the Desktop update and retry.");
            return candidate;
        }
    }
}
