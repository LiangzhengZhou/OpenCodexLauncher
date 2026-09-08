using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OpenCodexLauncherV2
{
    public static class DesktopAssociation
    {
        public static string Home(string configFile)
        {
            if (String.IsNullOrWhiteSpace(configFile) || !Path.IsPathRooted(configFile) ||
                !String.Equals(Path.GetFileName(configFile), "config.toml", StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.M("desktop.invalid"));
            return Path.GetDirectoryName(Path.GetFullPath(configFile));
        }
        // Read only the file explicitly selected by the user. Never search accounts,
        // create a missing external home, or copy providers/credentials between homes.
        public static LauncherSettings Preview(LauncherSettings current, string configFile)
        {
            if (!current.SetupCompleted) throw new InvalidOperationException(L.M("desktop.setup"));
            var home = Home(configFile);
            if (!File.Exists(configFile)) throw new IOException(L.M("desktop.missing"));
            TextFile.Read(configFile);
            var next = JsonData.Serializer().Deserialize<LauncherSettings>(JsonData.Serializer().Serialize(current));
            next.DesktopConfigPath = Path.Combine(home, "config.toml");
            return next;
        }
        public static void Validate(LauncherSettings settings)
        {
            if (settings.SetupCompleted && !String.IsNullOrWhiteSpace(settings.DesktopConfigPath))
                Preview(settings, settings.DesktopConfigPath);
        }
    }

    public sealed partial class MainWindow
    {
        TextBlock desktopTarget;
        UIElement DesktopPanel()
        {
            var panel = new StackPanel();
            panel.Children.Add(Text(L.M("desktop.explain")));
            desktopTarget = Text(""); panel.Children.Add(desktopTarget); UpdateDesktopTarget();
            panel.Children.Add(Btn(L.M("desktop.select"), PickDesktopConfig));
            panel.Children.Add(AsyncBtn(L.M("diag.button"), DiagnoseDesktop));
            return panel;
        }
        void UpdateDesktopTarget()
        {
            SetText(desktopTarget, L.F("desktop.target", paths.CodexConfig ?? "") + "\n" +
                L.M(String.IsNullOrWhiteSpace(settings.DesktopConfigPath) ? "desktop.unconfirmed" : "desktop.associated"));
        }
        void PickDesktopConfig()
        {
            if (gate.CurrentCount == 0) throw new InvalidOperationException(L.M("desktop.busy"));
            var dialog = new OpenFileDialog { Title = L.M("desktop.select"), Filter = "Codex config|config.toml", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            var next = DesktopAssociation.Preview(settings, dialog.FileName);
            if (!Confirm(L.F("desktop.confirm", next.DesktopConfigPath))) return;
            ApplyDesktopAssociation(next);
        }
        void ApplyDesktopAssociation(LauncherSettings next)
        {
            if (gate.CurrentCount == 0) throw new InvalidOperationException(L.M("desktop.busy"));
            // Revalidate after the confirmation, before saving a new association.
            next = DesktopAssociation.Preview(next, next.DesktopConfigPath);
            var resolved = PathResolver.Resolve(next); SetupService.Validate(resolved);
            PathResolver.Save(next);
            settings = next; paths = resolved; native.Clear(); catalogCache.Clear();
            nativeRefreshState = "not-requested";
            UpdateDesktopTarget(); LoadModels(); Log(L.M("desktop.saved"));
        }
    }
}
