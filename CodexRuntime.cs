using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace OpenCodexLauncherV2
{
    // A stable, non-sensitive diagnostic reason for a runtime that cannot be used.
    public sealed class NativeRuntimeException : Exception
    {
        public string Code { get; private set; }
        public NativeRuntimeException(string code, string message) : base(message) { Code = code; }
    }

    // Shared by GUI discovery and the independently launched stable Native helper.
    public static class CodexRuntime
    {
        static readonly object hashGate = new object();
        static readonly Dictionary<string, string> hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        // Kept for legacy/repair paths and explicit user selection. Automatic
        // compatibility decisions use the Desktop package core hash below.
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

        // The Desktop package's resources/codex.exe is the authoritative source
        // for the runtime that this Desktop build expects. Comparing its content
        // hash avoids comparing unrelated version schemes (CLI 0.164 vs Desktop 26.x).
        public static string DesktopPackageRoot()
        {
            if (LocalEnvironment.Current.IsIsolated) return null;
            try
            {
                const string repository = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
                using (var packages = Registry.CurrentUser.OpenSubKey(repository))
                {
                    if (packages == null) return null;
                    string bestRoot = null; Version bestVersion = null;
                    foreach (var name in packages.GetSubKeyNames())
                    {
                        if (!name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)) continue;
                        var version = PackageVersion(name);
                        if (version == null) continue;
                        using (var package = packages.OpenSubKey(name))
                        {
                            if (package == null) continue;
                            var root = package.GetValue("PackageRootFolder") as string;
                            if (String.IsNullOrWhiteSpace(root))
                                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", name);
                            root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            if (!Directory.Exists(root)) continue;
                            if (bestVersion == null || version > bestVersion) { bestVersion = version; bestRoot = root; }
                        }
                    }
                    return bestRoot;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        static Version PackageVersion(string packageName)
        {
            // OpenAI.Codex_26.1021.3705.0_x64__2p2nqsd0c76g0
            var parts = packageName.Split('_');
            if (parts.Length < 2) return null;
            Version value; return Version.TryParse(parts[1], out value) ? value : null;
        }

        public static string DesktopCore(string packageRoot)
        {
            if (String.IsNullOrWhiteSpace(packageRoot)) return null;
            var resources = Path.Combine(packageRoot, "app", "resources", "codex.exe");
            if (File.Exists(resources)) return resources;
            return Path.Combine(packageRoot, "resources", "codex.exe");
        }

        public static string HashFile(string path)
        {
            var info = new FileInfo(path);
            var key = info.FullName + "|" + info.Length.ToString() + "|" + info.LastWriteTimeUtc.Ticks.ToString();
            lock (hashGate)
            {
                string cached;
                if (hashCache.TryGetValue(key, out cached)) return cached;
            }
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, FileOptions.SequentialScan))
            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                lock (hashGate) { hashCache[key] = hash; }
                return hash;
            }
        }

        public static string FindMatchingRuntime(string root, string packageCore)
        {
            if (String.IsNullOrWhiteSpace(root) || String.IsNullOrWhiteSpace(packageCore) || !File.Exists(packageCore)) return null;
            try
            {
                var expectedLength = new FileInfo(packageCore).Length;
                var expectedHash = HashFile(packageCore);
                return Directory.GetFiles(root, "codex.exe", SearchOption.AllDirectories)
                    .Where(IsUsable)
                    .Where(x => { try { return new FileInfo(x).Length == expectedLength; } catch (IOException) { return false; } })
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal)
                    .FirstOrDefault(x => String.Equals(HashFile(x), expectedHash, StringComparison.OrdinalIgnoreCase));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        public static string FindCurrentDesktopRuntime(string packageRoot = null, string runtimeRoot = null)
        {
            try
            {
                var root = String.IsNullOrWhiteSpace(packageRoot) ? DesktopPackageRoot() : packageRoot;
                var core = DesktopCore(root);
                if (String.IsNullOrWhiteSpace(core) || !File.Exists(core)) return null;
                var localRoot = String.IsNullOrWhiteSpace(runtimeRoot) ? DesktopRoot : runtimeRoot;
                var matching = FindMatchingRuntime(localRoot, core);
                if (matching != null) return matching;
                // Desktop updates may install the new package before the local
                // runtime copy exists. The package core is the authoritative
                // current core and is complete when its host sibling is present.
                return IsUsable(core) ? core : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        public static string ResolveNative(NativeBridgeSettings settings, string root, Func<string, string> discover)
        {
            return ResolveNative(settings, root, discover, null);
        }

        // packageRoot is injectable for isolated tests. In production it is read
        // from the current user's AppModel registration.
        public static string ResolveNative(NativeBridgeSettings settings, string root, Func<string, string> discover, string packageRoot)
        {
            if (settings == null) throw new NativeRuntimeException("manifest-missing", "Native runtime manifest is missing.");

            if (settings.AutomaticRuntime == false)
            {
                if (IsUsable(settings.RealCodex)) return settings.RealCodex;
                throw new NativeRuntimeException("manual-runtime-incomplete",
                    "The explicitly selected Native Codex runtime is incomplete. Select a complete runtime in Launcher.");
            }

            // 3.1.0 manifests had no provenance. Preserve a complete path outside
            // Desktop's managed root as an explicit/legacy choice; a missing or
            // incomplete legacy path outside the managed root must not be replaced.
            if (settings.AutomaticRuntime == null && !IsDesktopRuntime(settings.RealCodex, root))
            {
                if (IsUsable(settings.RealCodex)) return settings.RealCodex;
                throw new NativeRuntimeException("manual-runtime-incomplete",
                    "The legacy Native Codex runtime is incomplete. Select a complete runtime in Launcher.");
            }

            var currentPackage = String.IsNullOrWhiteSpace(packageRoot) ? DesktopPackageRoot() : packageRoot;
            if (String.IsNullOrWhiteSpace(currentPackage) || !Directory.Exists(currentPackage))
                throw new NativeRuntimeException("desktop-package-unavailable",
                    "Cannot identify the installed Codex Desktop package. Finish the Desktop update and retry.");

            var core = DesktopCore(currentPackage);
            if (String.IsNullOrWhiteSpace(core) || !File.Exists(core))
                throw new NativeRuntimeException("desktop-core-missing",
                    "The installed Codex Desktop package has no bundled codex.exe. Repair the Desktop installation and retry.");

            var compatible = FindMatchingRuntime(root, core);
            if (compatible != null) return compatible;
            // A Desktop MSIX update can replace the package core before the
            // LocalAppData runtime copy is refreshed. Starting the helper would
            // otherwise deadlock: the helper rejects the old runtime, while
            // Desktop cannot run its new core to create the new copy. The
            // package's complete core is the same core Desktop just installed.
            if (IsUsable(core)) return core;
            throw new NativeRuntimeException("no-compatible-runtime",
                "No complete Codex runtime matches the installed Desktop package. Finish the Desktop update or run Native Prepare again.");
        }

        // Safe for logs: directory hash + length, never a user path or file content.
        public static string RuntimeId(string executable)
        {
            try
            {
                var directory = Path.GetFileName(Path.GetDirectoryName(executable));
                var length = new FileInfo(executable).Length;
                return (String.IsNullOrWhiteSpace(directory) ? "runtime" : directory) + "-" + length.ToString("x");
            }
            catch { return "runtime-unknown"; }
        }
    }
}
