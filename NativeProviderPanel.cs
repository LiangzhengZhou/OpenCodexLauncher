using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        bool helperRestartRequired;
        UIElement HelperUpdateNotice(bool hideCurrent, NativeActivation activation = null)
        {
            activation = activation ?? NativeActivation.Current;
            var panel = new StackPanel();
            var message = Text(""); panel.Children.Add(message);
            var button = AsyncBtn(L.M("native.updateHelper"), () => {
                if (!Confirm(L.M("native.updateConfirm"))) return Task.FromResult(0);
                activation.UpdateHelper(Assembly.GetExecutingAssembly().Location);
                helperRestartRequired = true;
                SetText(message, L.M("native.updateDone"));
                return Task.FromResult(0);
            });
            panel.Children.Add(button);
            Action refresh = () => {
                var state = activation.HelperStatus;
                panel.Visibility = hideCurrent && !helperRestartRequired && (state == "disabled" || state == "current") ? Visibility.Collapsed : Visibility.Visible;
                SetText(message, L.M(helperRestartRequired && state == "current" ? "native.updateDone" : "native.helper." + state));
                button.IsEnabled = state == "outdated" || state == "missing" || state == "recovery";
            };
            refresh(); panel.Loaded += delegate { refresh(); };
            return panel;
        }
        UIElement NativeProviderPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(SectionHeader(L.M("native.title"), L.M("native.note")));
            var body = new StackPanel();
            body.Children.Add(Text(L.M("provider.unifiedNote")));
            var providers = new ListBox { MinHeight = 80, MaxHeight = 220 };
            Action refresh = () => { providers.ItemsSource = NativeProviders.Read().Select(x => x.Provider.DisplayName + " · " + x.Models.Length + " models").ToList(); };
            body.Children.Add(providers);
            body.Children.Add(Btn(L.M("models.configure"), () => Navigate("providers"), true));
            panel.Loaded += delegate { try { refresh(); } catch { providers.ItemsSource = new[] { L.M("native.storeInvalid").ToString() }; } };
            panel.Children.Add(Card(body));
            var integration = new StackPanel(); integration.Children.Add(Text(L.M("native.enableNote")));
            integration.Children.Add(HelperUpdateNotice(false));
            var activationStatus = Text("");
            Action updateActivation = () => {
                var activation = NativeActivation.Current;
                SetText(activationStatus, L.M(activation.Enabled ? "native.enabled" : "native.disabled") + "\n" + L.M("native.runtime." + activation.RuntimeHealth));
            };
            updateActivation(); integration.Children.Add(activationStatus);
            integration.Children.Add(AsyncBtn(L.M("native.enablePersistent"), async () => {
                var helper = NativeActivation.InstallHelper(Assembly.GetExecutingAssembly().Location);
                await NativeActivation.Current.Enable(helper, NativeProviders.BridgePath,
                    () => NativeProviders.Prepare(paths, helper, settings.ReserveForceEnabled));
                updateActivation();
            }));
            integration.Children.Add(AsyncBtn(L.M("native.disablePersistent"), () => {
                NativeActivation.Current.Disable(); updateActivation(); return Task.FromResult(0);
            }));
            integration.Children.Add(AsyncBtn(L.M("native.prepare"), () => NativeProviders.Prepare(paths, NativeActivation.ResolveHelper(Assembly.GetExecutingAssembly().Location), settings.ReserveForceEnabled)));
            integration.Children.Add(AsyncBtn(L.M("native.launch"), async () => {
                var picker = new OpenFileDialog { Filter = "Codex Desktop|*.exe", CheckFileExists = true };
                if (picker.ShowDialog(this) != true) return;
                var executable = picker.FileName;
                if (Path.GetFullPath(executable).Equals(Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(executable).Equals(Path.GetFullPath(paths.Codex), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.M("native.desktopRequired"));
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
                    using (process) { if (process.MainModule.FileName.Equals(executable, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.M("native.closeFirst")); }
                await NativeProviders.Prepare(paths, NativeActivation.ResolveHelper(Assembly.GetExecutingAssembly().Location), settings.ReserveForceEnabled);
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
                start.EnvironmentVariables["CODEX_CLI_PATH"] = NativeActivation.ResolveHelper(Assembly.GetExecutingAssembly().Location);
                start.EnvironmentVariables["CODEX_HOME"] = paths.CodexHome;
                start.EnvironmentVariables["OPENCODEX_LAUNCHER_BRIDGE"] = NativeProviders.BridgePath;
                Process.Start(start);
            }));
            panel.Children.Add(Card(integration));
            try { refresh(); }
            catch { panel.Children.Add(Text(L.M("native.storeInvalid"))); }
            return panel;
        }
    }
}
