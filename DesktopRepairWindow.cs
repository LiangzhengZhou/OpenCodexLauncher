using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OpenCodexLauncherV2
{
    public sealed class DesktopSyncIncomplete : Exception { }
    public sealed partial class MainWindow
    {
        public Func<List<DesktopConfigCandidate>, DesktopConfigCandidate> DesktopCandidatePicker;
        public Func<PathSet, CancellationToken, Task<CommandResult>> DesktopSyncCommand;
        DesktopSyncResult lastDesktopSync;
        string lastDesktopSyncUtc;
        async Task AssociateAndSyncDesktop()
        {
            if (!settings.SetupCompleted) throw new InvalidOperationException(L.M("desktop.setup"));
            var input = DiagnosticInputsFactory();
            var discovery = Task.Run(() => DesktopCandidates.Find(settings, paths, input), life.Token);
            if (await Task.WhenAny(discovery, Task.Delay(TimeSpan.FromSeconds(12), life.Token)) != discovery)
            { life.Token.ThrowIfCancellationRequested(); throw new TimeoutException(L.M("repair.discovery-timeout")); }
            var candidates = await discovery;
            life.Token.ThrowIfCancellationRequested();
            if (candidates.Count == 0) throw new InvalidOperationException(L.M("repair.no-candidate"));
            var candidate = DesktopCandidatePicker == null ? PickCandidate(candidates) : DesktopCandidatePicker(candidates);
            if (candidate == null) throw new OperationCanceledException();
            life.Token.ThrowIfCancellationRequested();
            var next = DesktopCandidates.Confirm(settings, candidate, input);
            // Save an explicit association, keeping the existing OpenCodex provider store.
            ApplyDesktopAssociationCore(next);
            // Upstream owns its multi-file writes. Keep a private pre-sync snapshot for
            // recovery, but never blindly restore over possible external edits on failure.
            var backup = new FileTransaction(new[] { paths.CodexConfig, paths.Catalog, Path.Combine(paths.CodexHome, "models_cache.json"), paths.OcxConfig });
            Log(L.M("repair.backup") + backup.DirectoryPath);
            await Sync();
            await DiagnoseDesktop();
        }
        DesktopConfigCandidate PickCandidate(List<DesktopConfigCandidate> candidates)
        {
            ListBox list; var window = CreateDesktopCandidateWindow(candidates, out list);
            return window.ShowDialog() == true ? ((ListBoxItem)list.SelectedItem).Tag as DesktopConfigCandidate : null;
        }
        Window CreateDesktopCandidateWindow(List<DesktopConfigCandidate> candidates, out ListBox list)
        {
            var window = new Window { Owner = this, Width = 740, Height = 540, MinWidth = 540, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            L.Bind(window, Window.TitleProperty, L.M("repair.button"));
            var panel = new DockPanel();
            var note = Text(L.M("repair.explain")); DockPanel.SetDock(note, Dock.Top); panel.Children.Add(note);
            var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
            var choices = new ListBox { Margin = new Thickness(0, 12, 0, 12) };
            ScrollViewer.SetHorizontalScrollBarVisibility(choices, ScrollBarVisibility.Disabled);
            foreach (var candidate in candidates)
            {
                var label = Text(L.M("repair.role-" + candidate.Role) + "\n" + candidate.ConfigPath);
                choices.Items.Add(new ListBoxItem { Content = label, Tag = candidate, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
            var selectedList = choices;
            actions.Children.Add(Btn(L.M("repair.confirm"), () => { if (selectedList.SelectedItem != null) window.DialogResult = true; }));
            actions.Children.Add(Btn(L.M("repair.cancel"), () => window.DialogResult = false));
            choices.SelectedIndex = 0; panel.Children.Add(choices);
            window.Content = new Border { Background = System.Windows.Media.Brushes.White, Padding = new Thickness(18), Child = panel };
            list = choices; return window;
        }
        async Task<CommandResult> RunDesktopSyncCommand(CancellationToken token)
        {
            if (DesktopSyncCommand != null) return await DesktopSyncCommand(paths, token);
            var command = Commands.Ocx(paths.Ocx, "sync");
            return await runner.RunAsync(command.File, command.Arguments, command.Directory, TimeSpan.FromMinutes(5), token, Log, paths.OcxConfig, paths.CodexHome);
        }
    }
}
