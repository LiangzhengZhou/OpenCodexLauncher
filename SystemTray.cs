using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using Forms = System.Windows.Forms;

namespace OpenCodexLauncherV2
{
    // Keep the icon alive for the lifetime of the window, including while hidden.
    internal sealed class SystemTray : IDisposable
    {
        readonly Forms.NotifyIcon icon;
        readonly Forms.ContextMenuStrip menu;
        readonly System.Drawing.Icon image;
        readonly Forms.ToolStripMenuItem show, exit;
        bool notified, disposed;

        public SystemTray(Action restore, Action quit)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "OpenCodexLauncher.icon.ico"))
            using (var source = new System.Drawing.Icon(stream, 32, 32))
                image = (System.Drawing.Icon)source.Clone();
            menu = new Forms.ContextMenuStrip();
            show = new Forms.ToolStripMenuItem();
            exit = new Forms.ToolStripMenuItem();
            show.Click += delegate { restore(); };
            exit.Click += delegate { quit(); };
            menu.Items.Add(show);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exit);
            menu.Opening += delegate { RefreshLabels(); };
            icon = new Forms.NotifyIcon { Icon = image, Text = "OpenCodex Launcher", ContextMenuStrip = menu };
            icon.MouseClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) restore(); };
            icon.BalloonTipClicked += delegate { restore(); };
            RefreshLabels();
            try { icon.Visible = true; }
            catch { Dispose(); throw; }
        }

        void RefreshLabels()
        {
            show.Text = L.Get("tray.show");
            exit.Text = L.Get("tray.exit");
        }

        public void NotifyHidden()
        {
            if (disposed || notified || LocalEnvironment.Current.IsIsolated) return;
            notified = true;
            icon.ShowBalloonTip(4000, "OpenCodex Launcher", L.Get("tray.hidden"), Forms.ToolTipIcon.Info);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
            image.Dispose();
        }
    }

    public sealed partial class MainWindow
    {
        SystemTray tray;
        bool exitRequested, windowClosed;
        WindowState trayRestoreState = WindowState.Normal;

        internal void EnableSystemTray()
        {
            if (tray != null || windowClosed) return;
            tray = new SystemTray(RestoreFromTray, delegate { ExitLauncher(); });
            StateChanged += delegate
            {
                if (WindowState == WindowState.Minimized) HideToTray();
                else trayRestoreState = WindowState;
            };
            if (Application.Current != null) Application.Current.SessionEnding += TraySessionEnding;
        }

        void TraySessionEnding(object sender, SessionEndingCancelEventArgs e) { exitRequested = true; }

        void HandleWindowClosing(object sender, CancelEventArgs e)
        {
            if (tray == null || exitRequested) return;
            e.Cancel = true;
            HideToTray();
        }

        void HideToTray()
        {
            if (windowClosed || exitRequested || tray == null) return;
            HideProviderKey();
            Hide();
            tray.NotifyHidden();
        }

        void RestoreFromTray()
        {
            if (windowClosed) return;
            Show();
            WindowState = trayRestoreState;
            Activate();
        }

        // Both update and rollback must really close, even if already hidden.
        bool ExitLauncher()
        {
            if (windowClosed) return true;
            exitRequested = true;
            try { Close(); return windowClosed; }
            finally { if (!windowClosed) exitRequested = false; }
        }

        void HandleWindowClosed(object sender, EventArgs e)
        {
            windowClosed = true;
            if (Application.Current != null) Application.Current.SessionEnding -= TraySessionEnding;
            if (tray != null) { tray.Dispose(); tray = null; }
            timer.Stop();
            life.Cancel();
        }
    }
}
