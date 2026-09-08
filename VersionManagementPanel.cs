using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        UIElement VersionManagementPanel()
        {
            var panel = new StackPanel();
            panel.Children.Add(Text(L.M("version.title"), 18));
            panel.Children.Add(Text(L.M("version.description"), 13));
            panel.Children.Add(Text(L.Raw("Launcher 3.0.0"), 14));
            panel.Children.Add(Text(L.Raw("OpenCodex: " + (paths == null || String.IsNullOrEmpty(paths.Ocx) ? "not installed" : OpenCodexInstaller.ReadInstalledVersion(paths.Ocx) ?? "unknown")), 14));
            var actions = new WrapPanel();
            actions.Children.Add(Btn(L.M("version.history"), () => ModernDialog.Show(this, L.M("version.historyText"), L.M("version.title")), true));
            panel.Children.Add(actions);
            return Card(panel);
        }
    }
}
