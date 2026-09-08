using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        bool recoveryMode;
        Button LanguageButton()
        {
            return Btn("中文 / EN", () => {
                settings.Language = L.Language == "zh" ? "en" : "zh";
                L.SetLanguage(settings.Language);
                if (!recoveryMode) PathResolver.Save(settings);
            }, true);
        }
        StackPanel SetupPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(40), MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(LanguageButton());
            Content = new ScrollViewer { Background = System.Windows.Media.Brushes.White, Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return panel;
        }
        void BuildSetup()
        {
            var panel = SetupPanel();
            panel.Children.Add(Text(L.M("text.224"), 26));
            panel.Children.Add(Text(L.M("text.225"), 16));
            panel.Children.Add(InstallerPanel(true));
            var preview = Text("");
            PathSet detected = null;
            var confirm = Btn(L.M("text.226"), () => {
                if (detected == null) return;
                SetupService.Validate(detected);
                settings.OcxPath = detected.Ocx; settings.CodexPath = detected.Codex;
                settings.ConfigurationMode = "imported";
                CompleteSetup();
            });
            confirm.IsEnabled = false;
            panel.Children.Add(Btn(L.M("text.227"), () => {
                detected = PathResolver.Resolve(settings);
                SetupService.Validate(detected);
                var raw = JsonData.Read(detected.OcxConfig);
                var providers = JsonData.Object(JsonData.Value(raw, "providers"));
                SetText(preview, L.M("setup.preview") + "\nOpenCodex: " + detected.Ocx + "\nCodex: " + detected.Codex + "\n" + detected.OcxConfig + "\n" + detected.CodexConfig + "\n" + (providers == null ? "0" : providers.Count.ToString()));
                confirm.IsEnabled = true;
            }));
            panel.Children.Add(preview); panel.Children.Add(confirm);
            panel.Children.Add(Btn(L.M("text.228"), () => { settings.ConfigurationMode = "manual"; CompleteSetup(); }));
        }
        void CompleteSetup()
        {
            settings.SetupCompleted = true;
            paths = PathResolver.Resolve(settings);
            SetupService.Validate(paths);
            PathResolver.Save(settings);
            Build(); LoadModels();
            if (timer != null) timer.Start();
        }
        void BuildRecovery(string error)
        {
            recoveryMode = true;
            var panel = SetupPanel();
            panel.Children.Add(Text(L.M("text.229"), 26));
            panel.Children.Add(Text(L.M("text.230"), 16));
            panel.Children.Add(Text(error));
            panel.Children.Add(Btn(L.M("text.231"), () => { Directory.CreateDirectory(LocalEnvironment.Current.DataDirectory); Open(LocalEnvironment.Current.DataDirectory); }));
            panel.Children.Add(Btn(L.M("text.232"), () => {
                var next = PathResolver.Load();
                var nextPaths = next.SetupCompleted ? PathResolver.Resolve(next) : PathResolver.Empty();
                if (next.SetupCompleted) SetupService.Validate(nextPaths);
                settings = next; paths = nextPaths; recoveryMode = false;
                L.SetLanguage(settings.Language);
                if (!settings.SetupCompleted) BuildSetup(); else { Build(); LoadModels(); timer.Start(); }
            }));
        }
        TextBlock Text(LocalText value, int size = 13)
        { var block = Text("", size); L.Bind(block, TextBlock.TextProperty, value); return block; }
        void SetText(TextBlock block, LocalText value) { if (block != null && !life.IsCancellationRequested) L.Bind(block, TextBlock.TextProperty, value); }
        void SetText(TextBlock block, string value) { if (block != null && !life.IsCancellationRequested) block.Text = value; }
        Button Btn(LocalText value, Action action) { var b = Btn("", action); L.Bind(b, ContentControl.ContentProperty, value); return b; }
        Button Btn(LocalText value, Action action, bool secondary) { var b = Btn("", action, secondary); L.Bind(b, ContentControl.ContentProperty, value); return b; }
        Button AsyncBtn(LocalText value, Func<Task> action) { return Btn(value, async () => await Run(value, action)); }
        TextBox Field(Panel panel, LocalText label)
        {
            var field = Field(panel, "");
            L.Bind(panel.Children[panel.Children.Count - 2], TextBlock.TextProperty, label);
            return field;
        }
        Border SectionHeader(LocalText title, LocalText subtitle)
        { var panel = new StackPanel(); panel.Children.Add(Text(title,25)); panel.Children.Add(Text(subtitle)); return new Border { Child = panel, Margin = new Thickness(0,0,0,22) }; }
        void AddPage(string id, LocalText label, UIElement value)
        {
            pages[id] = new ScrollViewer { Content = value, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24) };
            var item = new ListBoxItem { Tag = id }; L.Bind(item, ContentControl.ContentProperty, label); navigation.Items.Add(item);
        }
        async Task ReserveTransaction(Func<Task> action)
        {
            var router = OpenCodexCompatibility.FindRouter(paths.Ocx);
            var snapshot = JsonData.Serializer().Serialize(settings);
            var transaction = new FileTransaction(new [] { router, paths.OcxConfig, paths.CodexConfig, PathResolver.SettingsPath() });
            try { await transaction.Step(action); await RestartOpenCodex(); transaction.Commit(); }
            catch (Exception failure)
            {
                try { transaction.Rollback(); settings = JsonData.Serializer().Deserialize<LauncherSettings>(snapshot); }
                catch (Exception recovery) { throw new IOException(L.M("reserve.recovery") + transaction.DirectoryPath + "\n" + Redactor.Apply(recovery.Message), failure); }
                throw new IOException(L.M("reserve.rolledBack") + transaction.DirectoryPath, failure);
            }
        }
    }
}
