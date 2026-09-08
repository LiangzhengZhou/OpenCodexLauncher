using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        public Func<DesktopDiagnosticInputs> DiagnosticInputsFactory = DesktopDiagnosticInputs.Local;
        async Task DiagnoseDesktop()
        {
            var report = await DesktopDiagnostics.CollectAsync(paths, settings, DiagnosticInputsFactory(), life.Token);
            if (life.IsCancellationRequested) return;
            ShowDesktopDiagnosticReport(report);
        }
        void ShowDesktopDiagnosticReport(DesktopDiagnosticReport report)
        {
            var window = new Window { Owner = this, Title = L.M("diag.title"), Width = 780, Height = 650,
                MinWidth = 540, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            L.Bind(window, Window.TitleProperty, L.M("diag.title"));
            var panel = new DockPanel();
            var note = Text(L.M("diag.explain")); DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
            var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
            var output = new LocalText(() => String.Join(Environment.NewLine, report.Findings.Select(code => "[" + code + "] " + L.Get("diag." + code))) + Environment.NewLine + Environment.NewLine + report.Json);
            actions.Children.Add(Btn(L.M("diag.copy"), () => { Clipboard.SetText(output.Value); L.Bind(window, Window.TitleProperty, L.M("diag.copied")); }));
            actions.Children.Add(Btn(L.M("diag.save"), () => {
                var dialog = new SaveFileDialog { Title = L.M("diag.save"), Filter = "Text (*.txt)|*.txt", FileName = "OpenCodexLauncher-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt" };
                if (dialog.ShowDialog(window) == true) File.WriteAllText(dialog.FileName, output.Value, new System.Text.UTF8Encoding(false));
            }));
            var box = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10) };
            L.Bind(box, TextBox.TextProperty, output); panel.Children.Add(box);
            window.Content = new Border { Background = System.Windows.Media.Brushes.White, Padding = new Thickness(18), Child = panel }; window.Show();
        }
    }
}
