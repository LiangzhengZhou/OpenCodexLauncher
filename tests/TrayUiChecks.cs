using System;
using System.IO;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OpenCodexLauncherV2;
using Forms = System.Windows.Forms;

partial class UiChecks
{
    static object InvokeTray(object target, string name, params object[] args)
    { return target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }

    static void TrayChecks(string output)
    {
        LocalEnvironment.UseIsolated(Path.Combine(output, "tray-environment"));
        var w = new MainWindow(); w.Show(); Pump();
        Check(Field<object>(w, "tray") == null, "isolated startup does not register a tray icon automatically");
        InvokeTray(w, "EnableSystemTray");
        var tray = Field<object>(w, "tray");
        var icon = Field<Forms.NotifyIcon>(tray, "icon");
        Check(icon.Visible && icon.Icon != null && icon.Icon.Width == 32, "tray uses the embedded 32px application icon");
        InvokeTray(w, "EnableSystemTray");
        Check(Object.ReferenceEquals(tray, Field<object>(w, "tray")), "repeated initialization keeps a single tray icon");
        var life = Field<CancellationTokenSource>(w, "life");
        var timer = Field<DispatcherTimer>(w, "timer"); timer.Start();
        w.Close(); Pump();
        Check(!w.IsVisible && !Field<bool>(w, "windowClosed") && icon.Visible, "window close hides to tray without destroying window or icon");
        Check(!life.IsCancellationRequested && timer.IsEnabled, "hiding keeps background work and polling alive");
        var show = Field<Forms.ToolStripMenuItem>(tray, "show");
        foreach (var language in new[] { "zh", "en" })
        {
            L.SetLanguage(language); InvokeTray(tray, "RefreshLabels");
            Check(show.Text == L.Get("tray.show") && Field<Forms.ToolStripMenuItem>(tray, "exit").Text == L.Get("tray.exit"), language + " tray menu uses the current UI language");
        }
        show.PerformClick(); Pump();
        Check(w.IsVisible && w.WindowState == WindowState.Normal, "tray menu restores the hidden window");
        w.WindowState = WindowState.Maximized; Pump();
        w.WindowState = WindowState.Minimized; Pump();
        Check(!w.IsVisible && icon.Visible, "minimize hides to tray");
        InvokeTray(icon, "OnMouseClick", new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 0, 0, 0)); Pump();
        Check(w.IsVisible && w.WindowState == WindowState.Maximized, "left click restores the previous maximized state");
        w.Close(); Pump();
        CancelEventHandler veto = delegate(object sender, CancelEventArgs e) { e.Cancel = true; };
        w.Closing += veto;
        Check(!(bool)InvokeTray(w, "ExitLauncher") && !life.IsCancellationRequested && icon.Visible, "replacement exit reports a veto even when already hidden");
        w.Closing -= veto;
        Check(!Field<bool>(w, "exitRequested"), "cancelled exit restores close-to-tray policy");
        show.PerformClick(); w.Close(); Pump();
        Field<Forms.ToolStripMenuItem>(tray, "exit").PerformClick(); Pump();
        Check(Field<bool>(w, "windowClosed") && Field<object>(w, "tray") == null && !icon.Visible, "tray Exit closes the window and removes the icon");
        Check(life.IsCancellationRequested && !timer.IsEnabled && Field<bool>(tray, "disposed"), "real exit cancels work and disposes tray resources");
        Check(!File.Exists(PathResolver.SettingsPath()), "tray operations do not create or change settings");

        var replacement = new MainWindow(); replacement.Show(); Pump(); InvokeTray(replacement, "EnableSystemTray");
        replacement.Close(); Pump();
        Check((bool)InvokeTray(replacement, "ExitLauncher") && Field<bool>(replacement, "windowClosed"), "shared update and rollback exit really closes a hidden launcher");
        var session = new MainWindow(); session.Show(); Pump(); InvokeTray(session, "EnableSystemTray");
        InvokeTray(session, "TraySessionEnding", new object[] { null, null });
        session.Close(); Pump();
        Check(Field<bool>(session, "windowClosed") && Field<object>(session, "tray") == null, "Windows session ending bypasses close-to-tray and releases the icon");
    }
}
