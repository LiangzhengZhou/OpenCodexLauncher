using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenCodexLauncherV2
{
    public static class ModernDialog
    {
        public static bool Show(Window owner, string message, string title, bool confirm = false)
        {
            var window = new Window { Owner = owner, Title = title, Width = 580, Height = 390, MinWidth = 420, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            ModernTheme.Apply(window);
            var panel = new DockPanel { Margin = new Thickness(26) };
            var heading = new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 18), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
            var copy = new Button { Margin = new Thickness(0, 0, 8, 8) }; L.Bind(copy, ContentControl.ContentProperty, L.M("workspace.copy")); copy.Click += delegate { Clipboard.SetText(Redactor.Apply(message)); }; actions.Children.Add(copy);
            if (confirm) { var cancel = new Button { IsCancel = true, Margin = new Thickness(0, 0, 8, 8) }; L.Bind(cancel, ContentControl.ContentProperty, L.M("workspace.cancel")); cancel.Click += delegate { window.DialogResult = false; }; actions.Children.Add(cancel); }
            var ok = new Button { IsDefault = true, MinWidth = 82, Margin = new Thickness(0, 0, 0, 8) }; L.Bind(ok, ContentControl.ContentProperty, L.M(confirm ? "workspace.confirm" : "workspace.ok")); ModernTheme.Primary(ok); ok.Click += delegate { window.DialogResult = true; }; actions.Children.Add(ok);
            var text = new TextBox { Text = Redactor.Apply(message), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White, BorderThickness = new Thickness(0) };
            panel.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), BorderBrush = new SolidColorBrush(Color.FromRgb(225, 230, 237)), BorderThickness = new Thickness(1), Padding = new Thickness(12), Child = text }); window.Content = panel;
            return window.ShowDialog() == true;
        }
    }
}
