using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("OpenCodex Launcher")]
[assembly: AssemblyVersion("2.6.5.0")]
[assembly: AssemblyFileVersion("2.6.5.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow : Window
    {
        readonly ConfigStore config = new ConfigStore();
        readonly AsyncProcessRunner runner = new AsyncProcessRunner();
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        readonly ObservableCollection<ModelOption> models = new ObservableCollection<ModelOption>();
        readonly ObservableCollection<ProviderModelChoice> choices = new ObservableCollection<ProviderModelChoice>();
        readonly List<ModelOption> native = new List<ModelOption>();
        readonly List<ModelOption> catalogCache = new List<ModelOption>();
        TextBlock modelSummary;
        bool catalogUnreadable;
        string nativeRefreshState = "not-requested";
        LauncherSettings settings;
        PathSet paths;
        TextBlock status, operation, providerStatus, healthText;
        TextBox providerId, providerName, providerUrl, providerDefault, search, logs;
        PasswordBox providerKey;
        ComboBox providerBox, adapterBox, modelBox, forceModelBox, strategyBox, runtimeBox, reserveTargetBox;
        CheckBox strictRouteBox, reserveForceBox;
        TextBlock reserveStatus;
        TextBlock quotaStatus;
        ContentControl content;
        ListBox navigation;
        readonly Dictionary<string, UIElement> pages = new Dictionary<string, UIElement>();
        string fetchedFor;
        bool populating;
        int refreshing, polling, diagnosing;
        FallbackBudget fallback = new FallbackBudget();
        LaunchRequest activeRequest;
        DispatcherTimer timer;
        RunningSession activeSession;
        RuntimeController activeRuntimeController;
        readonly Brush blue = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        readonly Brush ink = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        readonly Brush muted = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        readonly Brush page = new SolidColorBrush(Color.FromRgb(244, 247, 251));
        readonly Brush line = new SolidColorBrush(Color.FromRgb(226, 232, 240));

        public MainWindow()
        {
            string startupError = null;
            try { settings = PathResolver.Load(); } catch (Exception e) { startupError = Redactor.Apply(e.Message); settings = SetupService.Normalize(new LauncherSettings(), false); }
            L.SetLanguage(settings.Language); paths = PathResolver.Empty();
            if (startupError == null && settings.SetupCompleted) { try { paths = PathResolver.Resolve(settings); SetupService.Validate(paths); } catch (Exception e) { startupError = Redactor.Apply(e.Message); } }
            Title = "OpenCodex Launcher 2.6.5"; Width = 1180; Height = 850; MinWidth = 980; MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)); FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenCodexLauncher.icon.png"))
            { if (stream != null) { var icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); icon.Freeze(); Icon = icon; } }
            if (startupError != null) BuildRecovery(startupError); else if (!settings.SetupCompleted) BuildSetup(); else Build();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += async delegate { if (gate.CurrentCount == 0 || LocalEnvironment.Current.IsIsolated || recoveryMode || !settings.SetupCompleted || life.IsCancellationRequested || Interlocked.Exchange(ref polling, 1) != 0) return; try { await RefreshState(); await RefreshReserveStatus(); if (activeSession != null) await RefreshDiagnostics(); } catch (OperationCanceledException) { } catch (Exception e) { Log(e.Message); } finally { Interlocked.Exchange(ref polling, 0); } };
            Loaded += async delegate { if (startupError != null || !settings.SetupCompleted) return; LoadModels(); if (LocalEnvironment.Current.IsIsolated) return; await RefreshState(); await RefreshReserveStatus(); timer.Start(); };
            Closing += delegate { timer.Stop(); life.Cancel(); };
        }
        TextBlock Text(string value, int size = 13) { return new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Foreground = ink, FontSize = size, Margin = new Thickness(0, 5, 0, 10) }; }
        Button Btn(string label, Action action)
        {
            var b = new Button { Content = label, MinWidth = 116, MinHeight = 38, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 8), Background = blue, Foreground = Brushes.White, BorderBrush = blue, BorderThickness = new Thickness(1), FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand };
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            b.VerticalContentAlignment = VerticalAlignment.Center;
            b.Click += delegate { try { action(); } catch (Exception e) { Error(e.Message); } }; return b;
        }
        Button Btn(string label, Action action, bool secondary)
        {
            var b = Btn(label, action); if (secondary) { b.Background = Brushes.White; b.Foreground = ink; b.BorderBrush = line; } return b;
        }
        Button AsyncBtn(string label, Func<Task> action) { return Btn(label, async () => await Run(label, action)); }
        Border Card(UIElement value) { return new Border { Child = value, Background = Brushes.White, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 16) }; }
        TextBox Field(Panel panel, string label)
        {
            var caption = Text(label, 12); caption.Foreground = muted; caption.FontWeight = FontWeights.SemiBold; panel.Children.Add(caption); var field = new TextBox { MinHeight = 38, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 12), BorderBrush = line, BorderThickness = new Thickness(1) }; panel.Children.Add(field); return field;
        }
        Border SectionHeader(string title, string subtitle)
        {
            var panel = new StackPanel(); panel.Children.Add(Text(title, 25)); var note = Text(subtitle, 13); note.Foreground = muted; panel.Children.Add(note);
            return new Border { Child = panel, Margin = new Thickness(0, 0, 0, 22) };
        }
        void AddPage(string label, UIElement value)
        {
            pages[label] = new ScrollViewer { Content = value, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(30, 26, 30, 30) };
            navigation.Items.Add(label);
        }
        Style NavigationStyle()
        {
            var style = new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 12, 14, 12))); style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 4))); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 64, 175)))); selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); style.Triggers.Add(selected); return style;
        }
        void Build()
        {
            var root = new Grid { Background = page }; root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            var header = new Border { Background = Brushes.White, BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(26, 14, 30, 14) }; Grid.SetRow(header, 0); root.Children.Add(header);
            var headerGrid = new Grid(); headerGrid.ColumnDefinitions.Add(new ColumnDefinition()); headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; if (Icon != null) brand.Children.Add(new Image { Source = Icon, Width = 42, Height = 42, Margin = new Thickness(0, 0, 13, 0) }); var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; title.Children.Add(new TextBlock { Text = "OpenCodex Launcher", Foreground = ink, FontSize = 22, Margin = new Thickness(0, 0, 0, 4) }); title.Children.Add(Text(L.M("text.000"), 12)); brand.Children.Add(title); headerGrid.Children.Add(brand);
            var health = new Border { Background = new SolidColorBrush(Color.FromRgb(236, 253, 245)), BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(13, 7, 13, 7), VerticalAlignment = VerticalAlignment.Center }; healthText = Text(L.M("text.001")); health.Child = healthText; Grid.SetColumn(health, 1); var headerActions = new StackPanel { Orientation = Orientation.Horizontal }; headerActions.Children.Add(LanguageButton()); headerActions.Children.Add(health); Grid.SetColumn(headerActions, 1); headerGrid.Children.Add(headerActions); header.Child = headerGrid;
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) }); body.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(body, 1); root.Children.Add(body);
            var side = new Border { Background = ink, Padding = new Thickness(16, 24, 16, 18) }; Grid.SetColumn(side, 0); body.Children.Add(side); var sidePanel = new DockPanel(); var sideNote = Text(L.M("text.002"), 12); sideNote.Foreground = Brushes.LightGray; DockPanel.SetDock(sideNote, Dock.Bottom); sidePanel.Children.Add(sideNote); navigation = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, ItemContainerStyle = NavigationStyle() }; navigation.SelectionChanged += delegate { if (navigation.SelectedItem != null) { var pageId = (string)((ListBoxItem)navigation.SelectedItem).Tag; content.Content = pages[pageId]; if (pageId == "models") { try { LoadModels(); } catch (Exception) { SetText(modelSummary, L.M("models.configError")); } } } }; sidePanel.Children.Add(navigation); side.Child = sidePanel;
            content = new ContentControl { Background = page }; Grid.SetColumn(content, 1); body.Children.Add(content);
            operation = Text(L.M("text.003")); operation.Margin = new Thickness(22, 0, 22, 0); Grid.SetRow(operation, 2); root.Children.Add(operation);
            AddPage("overview", L.M("text.004"), Overview()); AddPage("providers", L.M("text.005"), Providers()); AddPage("models", L.M("text.006"), Models()); AddPage("force", L.M("text.007"), ForceLaunch()); AddPage("routes", L.M("text.008"), Routes()); AddPage("logs", L.M("text.009"), Logs()); AddPage("settings", L.M("text.010"), Settings()); navigation.SelectedIndex = 0; Content = root;
        }
        UIElement Overview()
        {
            var panel = new StackPanel(); panel.Children.Add(SectionHeader(L.M("text.011"), L.M("text.012")));
            panel.Children.Add(Card(Text(L.M("text.013"), 16)));
            var buttons = new WrapPanel(); buttons.Children.Add(AsyncBtn(L.M("text.014"), StartProxy)); buttons.Children.Add(AsyncBtn(L.M("text.015"), Sync));
            buttons.Children.Add(AsyncBtn(L.M("text.016"), async delegate { await RefreshModels(); await RefreshState(); })); buttons.Children.Add(AsyncBtn(L.M("text.017"), async () => Open((await OpenCodexEndpointResolver.ResolveAsync(paths.OcxConfig, life.Token)).BaseUrl))); panel.Children.Add(buttons);
            buttons.Children.Add(AsyncBtn(L.M("diag.button"), DiagnoseDesktop));
            buttons.Children.Add(AsyncBtn(L.M("repair.button"), AssociateAndSyncDesktop));
            status = Text(L.M("text.018"), 14); panel.Children.Add(Card(status)); return panel;
        }
        UIElement Providers()
        {
            populating = true;
            var panel = new StackPanel(); panel.Children.Add(SectionHeader(L.M("text.005"), L.M("text.019")));
            var editor = new StackPanel(); providerBox = new ComboBox { ItemsSource = config.Providers(paths.OcxConfig), Height = 34, Margin = new Thickness(0, 0, 0, 8) }; editor.Children.Add(providerBox);
            providerBox.SelectionChanged += delegate { if (!populating) Populate(providerBox.SelectedItem as ProviderOption); };
            editor.Children.Add(Btn(L.M("text.020"), () => Populate(null)));
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
            var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; var right = new StackPanel(); Grid.SetColumn(right, 1); grid.Children.Add(left); grid.Children.Add(right);
            providerId = Field(left, L.M("text.021")); providerName = Field(right, L.M("text.022"));
            providerUrl = Field(left, L.M("text.023"));
            right.Children.Add(Text(L.M("text.024"))); adapterBox = new ComboBox { ItemsSource = new [] { "openai-responses", "openai-chat" }, SelectedIndex = 0, Height = 34, Margin = new Thickness(0, 0, 0, 8) }; right.Children.Add(adapterBox);
            providerDefault = Field(left, L.M("text.025")); right.Children.Add(Text(L.M("text.026"))); providerKey = new PasswordBox { Height = 34, Padding = new Thickness(8) }; right.Children.Add(providerKey); editor.Children.Add(grid);
            var actions = new WrapPanel(); actions.Children.Add(AsyncBtn(L.M("text.027"), SaveProvider)); actions.Children.Add(AsyncBtn(L.M("text.028"), FetchModels)); editor.Children.Add(actions);
            providerStatus = Text(L.M("text.029")); editor.Children.Add(providerStatus); panel.Children.Add(Card(editor));
            var picker = new StackPanel(); search = Field(picker, L.M("text.030"));
            var list = new ListBox { ItemsSource = choices, MinHeight = 220, MaxHeight = 330, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            var template = new DataTemplate(typeof(ProviderModelChoice)); var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            check.SetBinding(CheckBox.ContentProperty, new Binding("DisplayName")); check.SetValue(CheckBox.PaddingProperty, new Thickness(5)); template.VisualTree = check; list.ItemTemplate = template; picker.Children.Add(list);
            search.TextChanged += delegate { var query = search.Text.Trim(); CollectionViewSource.GetDefaultView(choices).Filter = obj => { var row = (ProviderModelChoice)obj; return row.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || row.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0; }; };
            var select = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
            select.Children.Add(Btn(L.M("text.031"), () => { foreach (var row in CollectionViewSource.GetDefaultView(choices).Cast<ProviderModelChoice>()) row.Selected = true; }, true));
            select.Children.Add(Btn(L.M("text.032"), () => { foreach (var row in choices) row.Selected = false; }, true));
            select.Children.Add(AsyncBtn(L.M("text.033"), () => Import(false))); select.Children.Add(AsyncBtn(L.M("text.034"), () => Import(true))); picker.Children.Add(select); panel.Children.Add(Card(picker));
            foreach (var field in new [] { providerId, providerName, providerUrl, providerDefault }) field.TextChanged += delegate { Invalidate(); };
            providerKey.PasswordChanged += delegate { Invalidate(); }; adapterBox.SelectionChanged += delegate { Invalidate(); };
            populating = false;
            Populate(config.Providers(paths.OcxConfig).FirstOrDefault(p => p.Id == config.Provider(paths.OcxConfig) && p.Id != "openai") ?? config.Providers(paths.OcxConfig).FirstOrDefault(p => p.Id != "openai")); return panel;
        }
        void Populate(ProviderOption p)
        {
            populating = true; choices.Clear(); fetchedFor = null;
            providerId.Text = p == null ? "" : p.Id; providerName.Text = p == null ? "" : p.DisplayName; providerUrl.Text = p == null ? "" : p.BaseUrl; providerDefault.Text = p == null ? "" : p.DefaultModel;
            adapterBox.SelectedItem = p == null || String.IsNullOrEmpty(p.Adapter) ? "openai-responses" : p.Adapter; providerKey.Clear();
            SetText(providerStatus, p != null && p.Id == "openai" ? L.M("text.035") : L.M("text.036"));
            if (p != null && p.Id != "openai")
            {
                var selected = config.SelectedModels(paths.OcxConfig, p.Id);
                var currentConfig = config.ReadOcx(paths.OcxConfig);
                object providersValue;
                currentConfig.TryGetValue("providers", out providersValue);
                var providerRaw = JsonData.Object(JsonData.Value(JsonData.Object(providersValue), p.Id));
                var discovered = JsonData.Array(JsonData.Value(providerRaw, "discoveredModels")).OfType<string>();
                var saved = JsonData.Array(JsonData.Value(config.ReadOcx(paths.OcxConfig), "customModels")).Select(JsonData.Object).Where(x => JsonData.Text(x, "provider") == p.Id).Select(x => JsonData.Text(x, "modelId")).Concat(discovered).Concat(selected).Distinct(StringComparer.Ordinal);
                foreach (var id in saved) choices.Add(Choice(p, id, selected.Contains(id)));
                if (choices.Count > 0) fetchedFor = Fingerprint(p);
            }
            populating = false;
        }
        ProviderModelChoice Choice(ProviderOption p, string id, bool selected) { return new ProviderModelChoice { Id = id, DisplayName = ModelNames.Display(p.Id, p.DisplayName, id) + "    ·    " + id, Selected = selected }; }
        void Invalidate() { if (populating) return; choices.Clear(); fetchedFor = null; SetText(providerStatus, L.M("text.037")); }
        ProviderOption Form() { return new ProviderOption { Id = providerId.Text.Trim(), DisplayName = providerName.Text.Trim(), BaseUrl = ProviderClient.NormalizeBaseUrl(providerUrl.Text), Adapter = Convert.ToString(adapterBox.SelectedItem), DefaultModel = providerDefault.Text.Trim() }; }
        string Fingerprint(ProviderOption p) { return p.Id + "\n" + ProviderClient.NormalizeBaseUrl(p.BaseUrl) + "\n" + p.Adapter + "\n" + p.DisplayName; }
        async Task SaveProvider()
        {
            var p = Form(); var backup = await config.UpsertProviderAsync(paths.OcxConfig, p, providerKey.Password);
            populating = true; providerKey.Clear(); providerUrl.Text = p.BaseUrl; providerBox.ItemsSource = config.Providers(paths.OcxConfig); populating = false;
            SetText(providerStatus, L.M("text.038")); Log(L.M("text.039") + p.Id + L.M("text.040") + backup);
        }
        async Task FetchModels()
        {
            await SaveProvider(); var p = Form(); var selected = config.SelectedModels(paths.OcxConfig, p.Id);
            var fetched = await ProviderClient.FetchModelsAsync(p, CredentialStore.ForProvider(paths.OcxConfig, p.Id), life.Token);
            choices.Clear(); foreach (var id in fetched.Concat(selected).Distinct(StringComparer.Ordinal).OrderBy(x => x)) choices.Add(Choice(p, id, selected.Contains(id)));
            fetchedFor = Fingerprint(p); SetText(providerStatus, L.M("text.041") + fetched.Count + L.M("text.042") + selected.Count + L.M("text.043"));
        }
        async Task Import(bool sync)
        {
            var p = Form(); if (fetchedFor == null || fetchedFor != Fingerprint(p)) throw new InvalidOperationException(L.M("text.044"));
            var selected = choices.Where(x => x.Selected).Select(x => x.Id).ToArray();
            if (selected.Length == 0 && !Confirm(L.M("text.045"))) return;
            await config.SelectProviderModelsAsync(paths.OcxConfig, p.Id, p.DisplayName, selected, choices.Select(x => x.Id));
            var saved = config.SelectedModels(paths.OcxConfig, p.Id);
            if (!saved.SetEquals(selected)) throw new IOException(L.M("models.saveMismatch"));
            Populate(config.Providers(paths.OcxConfig).FirstOrDefault(x => x.Id == p.Id));
            LoadModels();
            if (selected.Any(id => !models.Any(m => m.Id == ModelNames.Slug(p.Id, id)))) throw new IOException(L.M("models.saveMismatch"));
            SetText(providerStatus, L.F("models.saved", saved.Count)); Log(L.F("models.saved", saved.Count));
            if (sync) await Sync();
        }
        UIElement Models()
        {
            var panel = new StackPanel(); panel.Children.Add(Text(L.M("text.048"), 14));
            modelSummary = Text(L.M("models.empty")); panel.Children.Add(modelSummary);
            modelBox = new ComboBox { ItemsSource = models, Height = 40, Margin = new Thickness(0, 10, 0, 18) }; panel.Children.Add(modelBox);
            var actions = new WrapPanel(); actions.Children.Add(AsyncBtn(L.M("text.049"), RefreshModels));
            actions.Children.Add(Btn(L.M("models.configure"), () => navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(x => (string)x.Tag == "providers")));
            actions.Children.Add(AsyncBtn(L.M("diag.button"), DiagnoseDesktop));
            actions.Children.Add(AsyncBtn(L.M("repair.button"), AssociateAndSyncDesktop));
            actions.Children.Add(Btn(L.M("text.050"), () => { var id = Selected(); ModelNames.ValidateId(id); var cwd = Directory.Exists(settings.WorkingDirectory) ? settings.WorkingDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); AsyncProcessRunner.StartVisible(paths.Codex, "-m " + Commands.Quote(id), cwd, paths); }));
            actions.Children.Add(AsyncBtn(L.M("text.051"), async delegate { var id = Selected(); if (Confirm(L.M("text.052") + id + "？")) { await EnsureProxyRoute(); await config.SetDefaultModelAsync(paths.CodexConfig, id); await Sync(); await RefreshState(); } }));
            actions.Children.Add(AsyncBtn(L.M("text.053"), async delegate {
                var id = Selected(); await RefreshModels(); var cataloged = File.Exists(paths.Catalog) && CatalogReader.ParseCatalog(TextFile.Read(paths.Catalog), false).Any(x => x.Id == id);
                MessageBox.Show(this, L.M("text.054") + id + L.M("text.055") + native.Any(x => x.Id == id) + L.M("text.056") + cataloged + L.M("text.057") + await Healthy() + L.M("text.058"), L.M("text.059"));
            }));
            actions.Children.Add(AsyncBtn(L.M("text.060"), async delegate { var id = Selected(); ModelNames.ValidateId(id); if (Confirm(L.M("text.061") + id + L.M("text.062"))) await Ocx(new [] { "access", "test", id, "--protocol", "responses" }); })); panel.Children.Add(actions);
            panel.Children.Add(Card(Text(L.M("text.063"), 14))); return panel;
        }
        UIElement ForceLaunch()
        {
            var panel = new StackPanel();
            panel.Children.Add(Card(Text(L.M("text.064"), 15)));
            panel.Children.Add(Text(L.M("selected.model")));
            forceModelBox = new ComboBox { ItemsSource = models, Height = 40, Margin = new Thickness(0, 0, 0, 14) };
            forceModelBox.SelectionChanged += delegate { if (forceModelBox.SelectedItem != null && modelBox != null) modelBox.SelectedItem = forceModelBox.SelectedItem; };
            panel.Children.Add(forceModelBox);
            panel.Children.Add(Text(L.M("text.065")));
            strategyBox = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 10), ItemsSource = new [] { "force", "auto", "follow-codex" } };
            strategyBox.SelectedItem = String.IsNullOrWhiteSpace(settings.LaunchStrategy) ? "follow-codex" : settings.LaunchStrategy; panel.Children.Add(strategyBox); panel.Children.Add(Text(L.M("text.066"), 12));
            panel.Children.Add(Text(L.M("text.067")));
            runtimeBox = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 10), ItemsSource = new [] { "auto", "codex-cli", "claude-code" } };
            runtimeBox.SelectedItem = String.IsNullOrWhiteSpace(settings.PreferredRuntime) ? "auto" : settings.PreferredRuntime; panel.Children.Add(runtimeBox);
            strictRouteBox = new CheckBox { Content = L.M("text.068"), IsChecked = settings.StrictRouteVerification, Margin = new Thickness(0, 0, 0, 10) }; L.Bind(strictRouteBox, ContentControl.ContentProperty, L.M("text.068")); panel.Children.Add(strictRouteBox);
            panel.Children.Add(ReserveForceCard());
            quotaStatus = Text(L.M("text.069"), 13); panel.Children.Add(Card(quotaStatus));
            var actions = new WrapPanel(); actions.Children.Add(Btn(L.M("text.070"), async () => await Run(L.M("text.007"), StartForce))); actions.Children.Add(AsyncBtn(L.M("text.071"), RefreshDiagnostics)); panel.Children.Add(actions);
            panel.Children.Add(Card(Text(L.M("text.072"), 14)));
            return panel;
        }
        UIElement ReserveForceCard()
        {
            var panel = new StackPanel();
            panel.Children.Add(Text(L.M("text.073"), 13));
            reserveForceBox = new CheckBox { Content = L.M("text.074"), IsChecked = settings.ReserveForceEnabled, IsEnabled = false, Margin = new Thickness(0, 0, 0, 8) }; L.Bind(reserveForceBox, ContentControl.ContentProperty, L.M("text.074")); panel.Children.Add(reserveForceBox);
            panel.Children.Add(Text(L.M("text.075")));
            reserveTargetBox = new ComboBox { Height = 36, Margin = new Thickness(0, 0, 0, 8) }; panel.Children.Add(reserveTargetBox);
            reserveStatus = Text(L.M("text.076"), 12); panel.Children.Add(reserveStatus);
            var actions = new WrapPanel();
            actions.Children.Add(AsyncBtn(L.M("text.077"), EnableReserveForce));
            actions.Children.Add(AsyncBtn(L.M("text.078"), DisableReserveForce));
            actions.Children.Add(AsyncBtn(L.M("text.079"), RefreshReserveStatus));
            panel.Children.Add(actions);
            return Card(panel);
        }
        ModelOption ReserveTarget()
        {
            var selected = reserveTargetBox == null ? null : reserveTargetBox.SelectedItem as ModelOption;
            if (selected == null || !selected.IsRouted || String.IsNullOrWhiteSpace(selected.Id)) throw new InvalidOperationException(L.M("text.080"));
            ModelNames.ValidateId(selected.Id);
            return selected;
        }
        async Task RestartOpenCodex()
        {
            if (!await Healthy()) await StartProxy();
            else await Ocx(new [] { "restart" }, false);
            for (var i = 0; i < 15; i++) { await Task.Delay(1000, life.Token); if (await Healthy()) return; }
            throw new TimeoutException(L.M("text.081"));
        }
        async Task EnableReserveForce()
        {
            var target = ReserveTarget();
            if (!Confirm(L.M("text.082"))) return;
            await ReserveTransaction(async delegate {
            Log(OpenCodexCompatibility.EnsureReservePreRouting(paths.Ocx));
            await EnsureProxyRoute();
            var legacy = config.BlockedModelRedirect(paths.OcxConfig, "gpt-reserve");
            await config.SetReserveForceAsync(paths.OcxConfig, target.Id, true);
            if (settings.ReserveForceOwned || String.Equals(legacy, target.Id, StringComparison.Ordinal)) await config.SetBlockedModelRedirectAsync(paths.OcxConfig, "gpt-reserve", "", false);
            settings.ReserveForceEnabled = true; settings.ReserveForceOwned = true; settings.ReserveForceTargetRoute = target.Id; settings.ReserveForceTargetModel = target.Id; settings.ReserveForceHadPreviousRedirect = false; settings.ReserveForcePreviousTargetModel = ""; settings.ReserveForceArmedAtUtc = DateTime.UtcNow.ToString("o"); PathResolver.Save(settings);
            });
            reserveForceBox.IsChecked = true; SetText(reserveStatus, L.M("text.083") + target.DisplayName + "（" + target.Id + L.M("text.084"));
            Log(L.M("text.085") + target.Id + L.M("text.086"));
        }
        async Task DisableReserveForce()
        {
            if (!settings.ReserveForceOwned && !settings.ReserveForceEnabled) { await RefreshReserveStatus(); return; }
            var target = String.IsNullOrWhiteSpace(settings.ReserveForceTargetRoute) ? settings.ReserveForceTargetModel : settings.ReserveForceTargetRoute;
            var current = config.ReserveForceTargetRoute(paths.OcxConfig);
            if (!String.IsNullOrWhiteSpace(target) && !String.IsNullOrWhiteSpace(current) && !String.Equals(current, target, StringComparison.Ordinal))
                throw new InvalidOperationException(L.M("text.087") + current + L.M("text.088"));
            if (!Confirm(L.M("text.089"))) return;
            await ReserveTransaction(async delegate {
            await config.SetReserveForceAsync(paths.OcxConfig, "", false);
            var legacy = config.BlockedModelRedirect(paths.OcxConfig, "gpt-reserve");
            if (settings.ReserveForceOwned || String.Equals(legacy, target, StringComparison.Ordinal)) await config.SetBlockedModelRedirectAsync(paths.OcxConfig, "gpt-reserve", "", false);
            settings.ReserveForceEnabled = false; settings.ReserveForceOwned = false; settings.ReserveForceTargetRoute = ""; settings.ReserveForceTargetModel = ""; settings.ReserveForceHadPreviousRedirect = false; settings.ReserveForcePreviousTargetModel = ""; settings.ReserveForceArmedAtUtc = ""; PathResolver.Save(settings);
            });
            reserveForceBox.IsChecked = false; SetText(reserveStatus, L.M("text.090")); Log(L.M("text.091"));
        }
        async Task RefreshReserveStatus()
        {
            if (reserveStatus == null || life.IsCancellationRequested) return;
            try
            {
                var configured = config.ReserveForceTargetRoute(paths.OcxConfig);
                var target = String.IsNullOrWhiteSpace(settings.ReserveForceTargetRoute) ? settings.ReserveForceTargetModel : settings.ReserveForceTargetRoute;
                if (!settings.ReserveForceEnabled) { SetText(reserveStatus, String.IsNullOrWhiteSpace(configured) ? L.M("text.092") : L.M("text.093") + configured); return; }
                var targetParts = (target ?? "").Split(new [] { '/' }, 2); var expectedProvider = targetParts.Length == 2 ? targetParts[0] : ""; var expectedModel = targetParts.Length == 2 ? targetParts[1] : "";
                var armed = String.Equals(configured, target, StringComparison.Ordinal);
                var text = armed ? L.M("text.083") + target : L.M("text.094") + target + L.M("text.095") + configured;
                if (armed && await Healthy())
                {
                    var since = DateTime.UtcNow.AddMinutes(-5); DateTime parsed; if (DateTime.TryParse(settings.ReserveForceArmedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out parsed)) since = parsed.ToUniversalTime();
                    var observation = await new OpenCodexClient(paths.OcxConfig).GetLatestObservationAsync(life.Token, since);
                    if (observation != null && String.Equals(observation.RequestedModel, "gpt-reserve", StringComparison.OrdinalIgnoreCase))
                    {
                        var status = observation.StatusCode.HasValue ? observation.StatusCode.Value.ToString() : L.M("text.096");
                        var matched = String.Equals(observation.Provider, expectedProvider, StringComparison.OrdinalIgnoreCase) && String.Equals(observation.ResolvedModel, expectedModel, StringComparison.OrdinalIgnoreCase) && observation.StatusCode.HasValue && observation.StatusCode.Value >= 200 && observation.StatusCode.Value < 300;
                        text += L.F("reserve.observation", observation.RequestedModel, target, observation.Provider, observation.ResolvedModel, status, matched ? L.M("text.097") : L.M("text.098"));
                    }
                    else text += L.M("text.099");
                }
                if (!life.IsCancellationRequested) SetText(reserveStatus, text);
            }
            catch (Exception e) { SetText(reserveStatus, L.M("text.100") + Redactor.Apply(e.Message)); }
        }
        async Task StartForce()
        {
            fallback = new FallbackBudget();
            var selected = (forceModelBox == null ? modelBox.SelectedItem : forceModelBox.SelectedItem) as ModelOption; if (selected == null) throw new InvalidOperationException(L.M("text.101"));
            var strategyText = Convert.ToString(strategyBox.SelectedItem); LaunchStrategy strategy = strategyText == "force" ? LaunchStrategy.Force : strategyText == "auto" ? LaunchStrategy.Auto : LaunchStrategy.FollowCodex;
            var runtimeText = Convert.ToString(runtimeBox.SelectedItem); RuntimeKind preferred = runtimeText == "claude-code" ? RuntimeKind.ClaudeCode : runtimeText == "auto" ? RuntimeKind.Direct : RuntimeKind.CodexCli;
            settings.LaunchStrategy = strategyText; settings.PreferredRuntime = runtimeText; settings.LastSelectedModel = selected.Id; settings.StrictRouteVerification = strictRouteBox.IsChecked == true; PathResolver.Save(settings);
            if (strategy == LaunchStrategy.FollowCodex) { await EnsureProxyRoute(); await config.SetDefaultModelAsync(paths.CodexConfig, selected.Id); await Sync(); return; }
            await StartProxy();
            var model = new LauncherModel { Id = selected.Id, RouteId = selected.Id, ModelId = selected.Id.Contains("/") ? selected.Id.Substring(selected.Id.IndexOf('/') + 1) : selected.Id, ProviderId = selected.Provider, DisplayName = selected.DisplayName, Routed = selected.IsRouted, NativeCodex = selected.IsNative, Availability = "unknown" };
            var client = new OpenCodexClient(paths.OcxConfig); activeRuntimeController = new RuntimeController(paths, client, life.Token); activeRequest = new LaunchRequest { FallbackBudget = fallback, ProjectPath = Directory.Exists(settings.WorkingDirectory) ? settings.WorkingDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), SelectedModel = model, Strategy = strategy, PreferredRuntime = preferred }; var session = await activeRuntimeController.LaunchAsync(activeRequest); activeSession = session;
            Log(L.M("text.102") + session.Runtime + L.M("text.103") + session.ModelRouteId + L.M("text.104")); MessageBox.Show(this, L.M("text.105") + session.Runtime + L.M("text.106") + session.ModelRouteId + L.M("text.107"), L.M("text.007"));
        }
        async Task RefreshDiagnostics()
        {
            if (life.IsCancellationRequested || Interlocked.Exchange(ref diagnosing, 1) != 0) return;
            try {
            var client = new OpenCodexClient(paths.OcxConfig); var healthy = await client.GetHealthAsync(life.Token); var nativeQuota = L.M("text.096");
            try { var q = await client.GetCodexQuotaAsync(life.Token); nativeQuota = L.Raw(Redactor.Apply(JsonData.Serializer().Serialize(q))); } catch (Exception e) { nativeQuota = L.M("text.108") + Redactor.Apply(e.Message); }
            life.Token.ThrowIfCancellationRequested();
            var route = activeSession == null ? L.M("text.109") : activeSession.ModelRouteId + "（" + (activeSession.Verified ? L.M("text.110") : L.M("text.111")) + "）";
            var retryRuntime = "";
            if (activeSession != null && activeRuntimeController != null && !activeSession.Verified && !activeSession.VerificationStopped)
            {
                try { var verified = await activeRuntimeController.VerifySessionRouteAsync(activeSession); route = activeSession.ModelRouteId + "（" + (verified ? L.M("text.110") : L.M("text.111")) + "）"; }
                catch (ModelInvariantViolation e)
                {
                    var failedRoute = activeSession.ModelRouteId;
                    activeSession.VerificationStopped = true;
                    if (strictRouteBox.IsChecked != true) { SetText(quotaStatus, L.M("text.112")); Log(e.Message); return; }
                    if (fallback.TryUse(true)) retryRuntime = activeSession.Runtime == RuntimeKind.ClaudeCode ? "codex-cli" : "claude-code";
                    try { if (activeSession.Process != null && !activeSession.Process.HasExited) AsyncProcessRunner.KillTree(activeSession.Process.Id); } catch { }
                    route = L.M("text.113") + Redactor.Apply(e.Message); Log(L.M("text.114") + failedRoute + L.M("text.115") + retryRuntime + "。");
                }
            }
            life.Token.ThrowIfCancellationRequested();
            if (retryRuntime != "")
            {
                if (runtimeBox != null) runtimeBox.SelectedItem = retryRuntime;
                try { activeRequest.PreferredRuntime = retryRuntime == "codex-cli" ? RuntimeKind.CodexCli : RuntimeKind.ClaudeCode; activeSession = await activeRuntimeController.LaunchAsync(activeRequest); route += L.M("text.116"); } catch (Exception retry) { route += L.M("text.117") + Redactor.Apply(retry.Message); }
            }
            SetText(quotaStatus, "OpenCodex：" + (healthy ? L.M("text.118") : L.M("text.119")) + L.M("quota.label") + nativeQuota + L.M("text.120") + route + L.M("text.121"));
            } finally { Interlocked.Exchange(ref diagnosing, 0); }
        }
        string Selected() { if (modelBox.SelectedItem == null) throw new InvalidOperationException(L.M("text.122")); return ((ModelOption)modelBox.SelectedItem).Id; }
        UIElement Routes()
        {
            var panel = new StackPanel(); panel.Children.Add(Text(L.M("text.123"), 14));
            panel.Children.Add(DesktopPanel());
            panel.Children.Add(AsyncBtn(L.M("text.124"), Sync));
            panel.Children.Add(AsyncBtn(L.M("text.125"), async delegate { if (Confirm(L.M("text.126"))) await Ocx(new [] { "restore" }); }));
            panel.Children.Add(AsyncBtn(L.M("text.127"), async delegate { if (Confirm(L.M("text.128"))) await Ocx(new [] { "restore", "back" }); }));
            panel.Children.Add(AsyncBtn(L.M("text.129"), async delegate {
                if (!Confirm(L.M("text.130"))) return;
                await EnsureProxyRoute();
                await Ocx(new [] { "sync", "--restart-desktop-app" });
                LoadModels(); Log(L.M("desktop.restartUnverified"));
            }));
            panel.Children.Add(AsyncBtn(L.M("text.131"), async delegate { if (Confirm(L.M("text.132"))) { await Ocx(new [] { "stop" }); await RefreshState(); } })); return panel;
        }
        UIElement Logs()
        {
            var panel = new StackPanel(); var actions = new WrapPanel();
            actions.Children.Add(AsyncBtn(L.M("text.133"), async delegate { var result = await Ocx(new [] { "logs", "--json" }, false); Log(result.Output); }));
            actions.Children.Add(Btn(L.M("text.134"), () => logs.Clear()));
            actions.Children.Add(Btn(L.M("text.135"), () => { var dialog = new SaveFileDialog { Filter = L.M("text.136"), FileName = "opencodex-sanitized.log" }; if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Redactor.Apply(logs.Text)); })); panel.Children.Add(actions);
            panel.Children.Add(Text(L.M("text.137")));
            logs = new TextBox { IsReadOnly = true, MinHeight = 450, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; panel.Children.Add(logs); return panel;
        }
        UIElement Settings()
        {
            var panel = new StackPanel(); panel.Children.Add(Text(L.M("text.138"), 14));
            panel.Children.Add(LauncherUpdatePanel());
            panel.Children.Add(InstallerPanel(false));
            panel.Children.Add(Btn(L.M("desktop.settings"), () => navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(x => (string)x.Tag == "routes")));
            panel.Children.Add(Btn(L.M("text.139"), () => PickPath(true))); panel.Children.Add(Btn(L.M("text.140"), () => PickPath(false)));
            var work = Field(panel, L.M("text.141")); work.Text = settings.WorkingDirectory ?? "";
            panel.Children.Add(Btn(L.M("text.142"), () => { if (!Directory.Exists(work.Text)) throw new IOException(L.M("text.143")); settings.WorkingDirectory = Path.GetFullPath(work.Text); PathResolver.Save(settings); }));
            panel.Children.Add(AsyncBtn(L.M("text.144"), async delegate { var result = await Ocx(new [] { "--version" }); MessageBox.Show(this, Redactor.Apply(result.Output), L.M("text.145")); }));
            panel.Children.Add(Btn(L.M("text.146"), () => Open("https://github.com/lidge-jun/opencodex/releases"))); return panel;
        }
        void PickPath(bool ocx)
        {
            var dialog = new OpenFileDialog { Filter = ocx ? L.M("text.147") : "Codex|codex.exe" };
            if (dialog.ShowDialog(this) != true) return;
            if (ocx) settings.OcxPath = dialog.FileName; else settings.CodexPath = dialog.FileName;
            PathResolver.Save(settings); paths = PathResolver.Resolve(settings);
        }
        async Task<CommandResult> Ocx(string[] args, bool log = true)
        {
            var command = Commands.Ocx(paths.Ocx, args);
            var result = await runner.RunAsync(command.File, command.Arguments, command.Directory, TimeSpan.FromMinutes(5), life.Token, log ? (Action<string>)Log : null, paths.OcxConfig, paths.CodexHome);
            if (!result.Succeeded) throw new InvalidOperationException(String.IsNullOrWhiteSpace(result.Error) ? L.M("text.148") + result.ExitCode : result.Error);
            return result;
        }
        async Task StartProxy()
        {
            if (await Healthy()) { Log(L.M("text.149")); return; }
            // --version exits in the Node shim; --help also loads the Bun CLI and
            // validates its imports/homes without starting a service or inference.
            await runner.CheckStartupAsync(Commands.Ocx(paths.Ocx, "--help"), paths.OcxConfig, paths.CodexHome, life.Token);
            using (var process = AsyncProcessRunner.StartDetached(Commands.Ocx(paths.Ocx, "start"), paths.OcxConfig, paths.CodexHome))
            {
                for (var i = 0; i < 30; i++) { await Task.Delay(1000, life.Token); if (await Healthy()) { await RefreshState(); return; } if (process.HasExited && process.ExitCode != 0) throw new InvalidOperationException(L.M("text.150") + process.ExitCode); }
            }
            throw new TimeoutException(L.M("text.151"));
        }
        async Task Sync()
        {
            try
            {
                DesktopSync.Selected(DesktopDiagnosticInputs.ReadLocal(paths.OcxConfig));
                await EnsureProxyRoute();
                lastDesktopSync = await DesktopSync.RunAsync(paths, RunDesktopSyncCommand, life.Token);
            }
            catch (OperationCanceledException) { throw; }
            catch { lastDesktopSync = new DesktopSyncResult { Code = "preparation-or-read-failed", Upstream = "unverified" }; }
            lastDesktopSyncUtc = DateTime.UtcNow.ToString("o");
            life.Token.ThrowIfCancellationRequested();
            if (!lastDesktopSync.Succeeded)
            {
                SetText(providerStatus, L.M("repair.incomplete"));
                Log(L.M("repair.incomplete") + " [" + lastDesktopSync.Code + "; " + lastDesktopSync.Upstream + "]");
                await DiagnoseDesktop();
                throw new DesktopSyncIncomplete();
            }
            LoadModels();
            SetText(providerStatus, L.M("text.152")); Log(L.M("text.153"));
        }
        async Task EnsureProxyRoute()
        {
            DesktopAssociation.Validate(settings);
            await config.SetCodexIntegrationAsync(paths.OcxConfig, true);
            await config.PrepareProxyRouteAsync(paths.CodexConfig);
        }
        void LoadModels()
        {
            SetupService.Validate(paths);
            var previous = modelBox.SelectedItem as ModelOption; var id = previous == null ? (String.IsNullOrWhiteSpace(settings.LastSelectedModel) ? config.CurrentModel(paths.CodexConfig) : settings.LastSelectedModel) : previous.Id;
            catalogUnreadable = false;
            var next = CatalogReader.Load(paths, config, native, code => catalogUnreadable = true, catalogCache); models.Clear(); foreach (var m in next) models.Add(m);
            var chosen = models.FirstOrDefault(x => x.Id == id) ?? models.FirstOrDefault(x => x.Id == config.CurrentModel(paths.CodexConfig)) ?? models.FirstOrDefault(x => x.Id == "gpt-6-astra") ?? models.FirstOrDefault(); modelBox.SelectedItem = chosen; if (forceModelBox != null) forceModelBox.SelectedItem = chosen;
            if (reserveTargetBox != null) { var routed = models.Where(x => x.IsRouted).ToList(); reserveTargetBox.ItemsSource = routed; reserveTargetBox.SelectedItem = routed.FirstOrDefault(x => x.Id == (String.IsNullOrWhiteSpace(settings.ReserveForceTargetRoute) ? settings.ReserveForceTargetModel : settings.ReserveForceTargetRoute)) ?? routed.FirstOrDefault(x => x.Id == settings.LastSelectedModel) ?? routed.FirstOrDefault(); }
            SetText(modelSummary, L.F("models.count", models.Count(x => x.IsRouted), models.Count(x => x.IsNative)) + "\n" +
                (catalogUnreadable ? L.M("models.catalogError") : models.Any(x => x.IsRouted) ? L.M("models.localReady") : L.M("models.empty")));
        }
        async Task RefreshModels()
        {
            // Show saved local models before any optional CLI work (which may fail or time out).
            LoadModels();
            if (!String.IsNullOrEmpty(paths.Codex))
            {
                try {
                var result = await runner.RunAsync(paths.Codex, "debug models", Directory.Exists(paths.CodexHome) ? paths.CodexHome : Environment.CurrentDirectory, TimeSpan.FromSeconds(30), life.Token, null, paths.OcxConfig, paths.CodexHome);
                if (life.IsCancellationRequested) return;
                if (result.Succeeded) { var list = CatalogReader.ParseCatalog(result.Output, true); native.Clear(); native.AddRange(list); nativeRefreshState = "ok"; }
                else { nativeRefreshState = "failed"; Log(L.M("models.cliError")); }
                } catch (OperationCanceledException) { throw; }
                catch (Exception) { if (life.IsCancellationRequested) return; nativeRefreshState = "failed"; Log(L.M("models.cliError")); }
            }
            else nativeRefreshState = "not-configured";
            LoadModels();
            if (nativeRefreshState == "failed") SetText(modelSummary, L.F("models.count", models.Count(x => x.IsRouted), models.Count(x => x.IsNative)) + "\n" + L.M("models.cliError"));
        }
        async Task<bool> Healthy()
        {
            try { await OpenCodexEndpointResolver.ResolveAsync(paths.OcxConfig, life.Token); return true; } catch { return false; }
        }
        async Task RefreshState()
        {
            if (life.IsCancellationRequested || Interlocked.Exchange(ref refreshing, 1) != 0) return;
            try { var healthy = await Healthy(); if (healthText != null) { SetText(healthText, healthy ? L.M("text.155") : L.M("text.156")); healthText.Foreground = healthy ? new SolidColorBrush(Color.FromRgb(5, 150, 105)) : new SolidColorBrush(Color.FromRgb(220, 38, 38)); } SetText(status, L.M("text.157") + (healthy ? L.M("text.118") : L.M("text.119")) + L.M("text.158") + config.Provider(paths.OcxConfig) + L.M("text.159") + config.CurrentModel(paths.CodexConfig) + L.M("text.055") + native.Count + L.M("text.160") + models.Count + L.M("text.161") + paths.Ocx + "\nCodex：" + paths.Codex + L.M("text.162") + paths.OcxConfig + L.M("text.163") + paths.Catalog); }
            catch (Exception e) { SetText(status, L.M("text.164") + Redactor.Apply(e.Message)); }
            finally { Interlocked.Exchange(ref refreshing, 0); }
        }
        Task Run(string label, Func<Task> action) { return Run(L.Raw(label), action); }
        async Task Run(LocalText label, Func<Task> action)
        {
            if (!await gate.WaitAsync(0)) return;
            if (navigation != null) navigation.IsEnabled = false; SetText(operation, label + "…");
            try { await action(); if (!life.IsCancellationRequested) SetText(operation, label + L.M("text.165")); }
            catch (OperationCanceledException) { if (!life.IsCancellationRequested) SetText(operation, label + L.M("text.166")); }
            catch (DesktopSyncIncomplete) { if (!life.IsCancellationRequested) SetText(operation, L.M("repair.incomplete")); }
            catch (Exception e) { if (!life.IsCancellationRequested) { SetText(operation, label + L.M("text.167")); Error(e.Message); } }
            finally { gate.Release(); if (navigation != null && !life.IsCancellationRequested) navigation.IsEnabled = true; }
        }
        void Log(string text)
        {
            if (life.IsCancellationRequested) return;
            if (!Dispatcher.CheckAccess()) { if (!life.IsCancellationRequested) Dispatcher.BeginInvoke(new Action(() => Log(text))); return; }
            if (logs == null) return; logs.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + Redactor.Apply(text) + Environment.NewLine);
            if (logs.Text.Length > 200000) logs.Text = logs.Text.Substring(logs.Text.Length - 200000);
        }
        bool Confirm(string text) { return MessageBox.Show(this, text, "OpenCodex Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }
        void Error(string text) { Log(text); if (!life.IsCancellationRequested) MessageBox.Show(this, Redactor.Apply(text), L.M("text.168"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        void Open(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    }
    public static class AppEntry
    {
        [STAThread] public static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--apply-launcher-update") { Environment.ExitCode = LauncherUpdater.RunHelper(args[1]); return; }
            if (args.Length == 2 && args[0] == "--isolated") LocalEnvironment.UseIsolated(args[1]);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            try { app.Run(new MainWindow()); }
            catch (Exception e) { MessageBox.Show(Redactor.Apply(e.Message), L.M("text.169")); }
        }
    }
}
