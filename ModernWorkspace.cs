using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        sealed class ProviderDraft
        {
            public ProviderOption Provider;
            public string Key, Fingerprint, LastFetch, Query;
            public bool KeyEdited, Stale, FetchFailed, SelectionDirty, OnlySelected;
            public List<ProviderModelChoice> Rows;
        }
        readonly Dictionary<string, ProviderDraft> providerDrafts = new Dictionary<string, ProviderDraft>(StringComparer.Ordinal);
        string editingProvider, lastFetch;
        bool keyEdited, keySyncing, modelStale, fetchFailed, selectionDirty, onlySelected, workspaceReady;
        TextBox providerKeyVisible;
        Button providerKeyToggle, allModelsButton, selectedModelsButton;
        ComboBox modelProviderBox;
        TextBlock providerHeading, providerMeta, modelCounts, modelWorkspaceHeading, modelWorkspaceStatus, modelEmpty;
        ListBox modelChoiceList;
        public Func<ProviderOption, string, CancellationToken, Task<List<string>>> ModelFetch = ProviderClient.FetchModelsAsync;

        ComboBox ProviderSelector()
        {
            var box = new ComboBox { MinHeight = 44, Margin = new Thickness(0, 0, 0, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            // Use an explicit text template for both the popup and the closed selector.
            var template = new DataTemplate(typeof(ProviderOption));
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty, new Binding());
            label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            template.VisualTree = label; box.ItemTemplate = template;
            return box;
        }
        StackPanel CardBody(string number, string key)
        {
            var body = new StackPanel();
            var eyebrow = Text(number, 11); eyebrow.Foreground = muted; body.Children.Add(eyebrow);
            var heading = Text(L.M(key), 20); heading.FontWeight = FontWeights.Bold; body.Children.Add(heading); return body;
        }
        UIElement Providers()
        {
            populating = true;
            var pagePanel = new StackPanel(); pagePanel.Children.Add(SectionHeader(L.M("workspace.providers"), L.M("workspace.providersNote")));
            var summary = CardBody("01 / PROVIDER", "workspace.current");
            var selector = new DockPanel(); var add = Btn(L.M("text.020"), () => SwitchProvider(null), true); DockPanel.SetDock(add, Dock.Right); selector.Children.Add(add);
            providerBox = ProviderSelector(); selector.Children.Add(providerBox); summary.Children.Add(selector);
            providerHeading = Text("", 23); providerHeading.FontWeight = FontWeights.Bold;
            providerMeta = Text(""); providerMeta.Foreground = muted; summary.Children.Add(providerMeta); pagePanel.Children.Add(Card(summary));

            var connection = CardBody("02 / CONNECTION", "workspace.connection");
            var fields = new Grid(); fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition());
            fields.RowDefinitions.Add(new RowDefinition()); fields.RowDefinitions.Add(new RowDefinition());
            var idCell = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; var nameCell = new StackPanel();
            var urlCell = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; var protocolCell = new StackPanel();
            fields.Children.Add(idCell); fields.Children.Add(nameCell); fields.Children.Add(urlCell); fields.Children.Add(protocolCell);
            Grid.SetColumn(nameCell, 1); Grid.SetRow(urlCell, 1); Grid.SetRow(protocolCell, 1); Grid.SetColumn(protocolCell, 1);
            providerId = Field(idCell, L.M("text.021")); providerName = Field(nameCell, L.M("text.022")); providerUrl = Field(urlCell, L.M("text.023"));
            protocolCell.Children.Add(Text(L.M("text.024"), 12)); adapterBox = new ComboBox { ItemsSource = new[] { "openai-responses", "openai-chat" }, MinHeight = 44, Margin = new Thickness(0, 0, 0, 12), SelectedIndex = 0 }; protocolCell.Children.Add(adapterBox); connection.Children.Add(fields);
            connection.Children.Add(Text(L.Raw("API Key"), 12));
            var keyRow = new Grid(); keyRow.ColumnDefinitions.Add(new ColumnDefinition()); keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var keyHost = new Grid { Margin = new Thickness(0, 0, 10, 12) }; providerKey = new PasswordBox { MinHeight = 44, Padding = new Thickness(12, 8, 12, 8) };
            providerKeyVisible = new TextBox { MinHeight = 44, Visibility = Visibility.Collapsed }; keyHost.Children.Add(providerKey); keyHost.Children.Add(providerKeyVisible); keyRow.Children.Add(keyHost);
            providerKeyToggle = Btn(L.M("workspace.showKey"), ToggleProviderKey, true); Grid.SetColumn(providerKeyToggle, 1); keyRow.Children.Add(providerKeyToggle); connection.Children.Add(keyRow);
            var keyActions = new WrapPanel(); keyActions.Children.Add(Btn(L.M("workspace.clearKey"), async () => await Run(L.M("workspace.clearKey"), ClearProviderKey), true)); connection.Children.Add(keyActions);
            connection.Children.Add(Text(L.M("workspace.keyNote"), 12));
            connection.PreviewKeyDown += async (sender, e) => { if (e.Key == Key.Enter && !(e.OriginalSource is Button) && !adapterBox.IsDropDownOpen) { e.Handled = true; await Run(L.M("text.027"), SaveProvider); } };

            var state = CardBody("03 / MODELS", "workspace.modelState"); modelCounts = Text("", 22); state.Children.Add(modelCounts);
            providerStatus = Text(L.M("text.036")); state.Children.Add(providerStatus);
            state.Children.Add(Text(L.M("workspace.modelsNote")));
            var actions = new WrapPanel(); actions.Children.Add(AsyncBtn(L.M("text.028"), FetchModels)); var save = AsyncBtn(L.M("text.027"), SaveProvider); Secondary(save); actions.Children.Add(save); state.Children.Add(actions);
            state.Children.Add(Btn(L.M("workspace.manage"), () => Navigate("models"), true));
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
            var connectionCard = Card(connection); var stateCard = Card(state); grid.Children.Add(connectionCard); grid.Children.Add(stateCard);
            grid.SizeChanged += delegate { bool narrow = grid.ActualWidth < 920; grid.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : 1.6, GridUnitType.Star); grid.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star); Grid.SetColumn(stateCard, narrow ? 0 : 1); Grid.SetRow(stateCard, narrow ? 1 : 0); connectionCard.Margin = new Thickness(0, 0, narrow ? 0 : 16, 16); };
            pagePanel.Children.Add(grid);
            providerBox.SelectionChanged += delegate { if (!populating) SwitchProvider(providerBox.SelectedItem as ProviderOption); };
            foreach (var field in new[] { providerId, providerUrl }) field.TextChanged += delegate { Invalidate(); };
            providerName.TextChanged += delegate { if (!populating) { UpdateWorkspaceState(); } };
            adapterBox.SelectionChanged += delegate { Invalidate(); };
            providerKey.PasswordChanged += delegate { if (populating || keySyncing) return; keyEdited = true; Redactor.RegisterSecret(providerKey.Password); if (providerKeyVisible.Visibility == Visibility.Visible) { keySyncing = true; providerKeyVisible.Text = providerKey.Password; keySyncing = false; } Invalidate(); };
            providerKeyVisible.TextChanged += delegate { if (populating || keySyncing || providerKeyVisible.Visibility != Visibility.Visible) return; keySyncing = true; providerKey.Password = providerKeyVisible.Text; keySyncing = false; keyEdited = true; Redactor.RegisterSecret(providerKey.Password); Invalidate(); };
            choices.CollectionChanged += delegate { UpdateWorkspaceState(); };
            populating = false; workspaceReady = true; RefreshProviderSelectors(null);
            Populate(config.Providers(paths.OcxConfig).FirstOrDefault(p => p.Id == config.Provider(paths.OcxConfig) && p.Id != "openai") ?? config.Providers(paths.OcxConfig).FirstOrDefault(p => p.Id != "openai"));
            return pagePanel;
        }
        void Secondary(Button b) { b.Background = Brushes.White; b.Foreground = ink; b.BorderBrush = line; }
        void Navigate(string id) { navigation.SelectedItem = navigation.Items.Cast<ListBoxItem>().Single(x => (string)x.Tag == id); }
        void RememberDraft()
        {
            if (!workspaceReady) return;
            providerDrafts[editingProvider ?? ""] = new ProviderDraft { Provider = DraftForm(), Key = providerKey.Password, KeyEdited = keyEdited, Fingerprint = fetchedFor, Stale = modelStale, FetchFailed = fetchFailed, LastFetch = lastFetch, Rows = choices.ToList(), SelectionDirty = selectionDirty, Query = search == null ? "" : search.Text, OnlySelected = onlySelected };
        }
        ProviderOption DraftForm() { return new ProviderOption { Id = providerId.Text.Trim(), DisplayName = providerName.Text.Trim(), BaseUrl = providerUrl.Text.Trim(), Adapter = Convert.ToString(adapterBox.SelectedItem) }; }
        void SwitchProvider(ProviderOption p) { RememberDraft(); Populate(p); }
        void RefreshProviderSelectors(string id)
        {
            bool was = populating; populating = true;
            var items = config.Providers(paths.OcxConfig).Where(p => p.Id != "openai").ToList();
            providerBox.ItemsSource = items; providerBox.SelectedItem = items.FirstOrDefault(p => p.Id == id);
            if (modelProviderBox != null) { modelProviderBox.ItemsSource = items; modelProviderBox.SelectedItem = items.FirstOrDefault(p => p.Id == id); }
            populating = was;
        }
        void Populate(ProviderOption p)
        {
            HideProviderKey(); populating = true;
            try {
                editingProvider = p == null ? null : p.Id; ProviderDraft draft;
                providerDrafts.TryGetValue(editingProvider ?? "", out draft);
                var form = draft == null ? p : draft.Provider;
                providerId.Text = form == null ? "" : form.Id; providerName.Text = form == null ? "" : form.DisplayName; providerUrl.Text = form == null ? "" : form.BaseUrl;
                // Existing IDs identify storage and routes; create a new provider to use another ID.
                providerId.IsReadOnly = editingProvider != null;
                adapterBox.SelectedItem = form == null || String.IsNullOrEmpty(form.Adapter) ? "openai-responses" : form.Adapter;
                providerKey.Password = draft != null ? draft.Key : p == null ? "" : CredentialStore.ForProvider(paths.OcxConfig, p.Id); Redactor.RegisterSecret(providerKey.Password);
                keyEdited = draft != null && draft.KeyEdited; modelStale = draft != null && draft.Stale; fetchFailed = draft != null && draft.FetchFailed; selectionDirty = draft != null && draft.SelectionDirty; lastFetch = draft == null ? null : draft.LastFetch; fetchedFor = draft == null ? null : draft.Fingerprint;
                choices.Clear();
                if (draft != null) { foreach (var row in draft.Rows) choices.Add(row); }
                else if (p != null) {
                    var root = config.ReadOcx(paths.OcxConfig); var raw = JsonData.Object(JsonData.Value(JsonData.Object(JsonData.Value(root, "providers")), p.Id));
                    var selected = config.SelectedModels(paths.OcxConfig, p.Id);
                    var ids = JsonData.Array(JsonData.Value(raw, "discoveredModels")).OfType<string>().Concat(JsonData.Array(JsonData.Value(root, "customModels")).Select(JsonData.Object).Where(x => JsonData.Text(x, "provider") == p.Id).Select(x => JsonData.Text(x, "modelId"))).Concat(selected).Distinct(StringComparer.Ordinal);
                    foreach (var id in ids.OrderBy(x => x)) choices.Add(Choice(p, id, selected.Contains(id)));
                    lastFetch = JsonData.Text(raw, "launcherModelsFetchedAt");
                    var savedFingerprint = JsonData.Text(raw, "launcherModelsConnection");
                    fetchedFor = choices.Count == 0 ? null : Fingerprint(p);
                    modelStale = choices.Count > 0 && savedFingerprint != Fingerprint(p);
                }
                if (search != null) search.Text = draft == null ? "" : draft.Query ?? ""; onlySelected = draft != null && draft.OnlySelected;
                RefreshProviderSelectors(editingProvider);
            } finally { populating = false; }
            UpdateWorkspaceState(); ApplyModelFilter();
        }
        ProviderModelChoice Choice(ProviderOption p, string id, bool selected)
        {
            var row = new ProviderModelChoice { Id = id, DisplayName = ModelNames.Display(p.Id, p.DisplayName, id), Route = ModelNames.Slug(p.Id, id), Selected = selected };
            row.PropertyChanged += delegate { if (populating) return; selectionDirty = true; UpdateWorkspaceState(); if (onlySelected) Dispatcher.BeginInvoke(new Action(ApplyModelFilter)); }; return row;
        }
        void Invalidate() { if (populating) return; modelStale = true; fetchedFor = null; UpdateWorkspaceState(); }
        ProviderOption Form() { var p = DraftForm(); p.BaseUrl = ProviderClient.NormalizeBaseUrl(p.BaseUrl); return p; }
        string Fingerprint(ProviderOption p) { return p.Id + "\n" + ProviderClient.NormalizeBaseUrl(p.BaseUrl) + "\n" + p.Adapter; }
        async Task SaveProvider()
        {
            var p = Form();
            await config.UpsertProviderAsync(paths.OcxConfig, p, keyEdited ? providerKey.Password : null);
            if (modelStale) await config.MarkProviderModelsStaleAsync(paths.OcxConfig, p.Id);
            populating = true; providerUrl.Text = p.BaseUrl; providerKey.Password = CredentialStore.ForProvider(paths.OcxConfig, p.Id);
            if (providerKeyVisible.Visibility == Visibility.Visible) providerKeyVisible.Text = providerKey.Password;
            populating = false; keyEdited = false;
            if (editingProvider == null) providerDrafts.Remove(""); editingProvider = p.Id; providerId.IsReadOnly = true;
            RefreshProviderSelectors(p.Id); RememberDraft(); UpdateWorkspaceState();
            SetText(providerStatus, L.M("text.038")); Log(L.M("text.039") + p.Id);
        }
        async Task FetchModels()
        {
            await SaveProvider(); var p = Form(); var selected = new HashSet<string>(choices.Where(x => x.Selected).Select(x => x.Id), StringComparer.Ordinal);
            try {
                var fetched = await ModelFetch(p, CredentialStore.ForProvider(paths.OcxConfig, p.Id), life.Token);
                life.Token.ThrowIfCancellationRequested(); var time = DateTime.UtcNow.ToString("o");
                var all = fetched.Concat(choices.Select(x => x.Id)).Concat(selected).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToList();
                await config.RecordProviderModelsAsync(paths.OcxConfig, p.Id, all, Fingerprint(p), time);
                populating = true; choices.Clear(); foreach (var id in all) choices.Add(Choice(p, id, selected.Contains(id))); populating = false;
                fetchedFor = Fingerprint(p); modelStale = false; fetchFailed = false; lastFetch = time; RememberDraft(); UpdateWorkspaceState(); ApplyModelFilter();
            } catch { populating = false; fetchFailed = true; RememberDraft(); UpdateWorkspaceState(); throw; }
        }
        async Task Import(bool sync)
        {
            if (editingProvider == null) throw new InvalidOperationException(L.M("workspace.saveFirst"));
            var p = config.Providers(paths.OcxConfig).Single(x => x.Id == editingProvider);
            var selected = choices.Where(x => x.Selected).Select(x => x.Id).ToArray();
            if (selected.Length == 0 && !Confirm(L.M("text.045"))) return;
            await config.SelectProviderModelsAsync(paths.OcxConfig, p.Id, p.DisplayName, selected, choices.Select(x => x.Id));
            if (!config.SelectedModels(paths.OcxConfig, p.Id).SetEquals(selected)) throw new System.IO.IOException(L.M("models.saveMismatch"));
            selectionDirty = false; RememberDraft(); LoadModels(); UpdateWorkspaceState();
            SetText(providerStatus, L.F("models.saved", selected.Length)); Log(L.F("models.saved", selected.Length)); if (sync) await Sync();
        }
        void ToggleProviderKey()
        {
            if (providerKeyVisible.Visibility == Visibility.Visible) { HideProviderKey(); providerKey.Focus(); return; }
            keySyncing = true; providerKeyVisible.Text = providerKey.Password; keySyncing = false;
            providerKey.Visibility = Visibility.Collapsed; providerKeyVisible.Visibility = Visibility.Visible; L.Bind(providerKeyToggle, ContentControl.ContentProperty, L.M("workspace.hideKey")); providerKeyVisible.Focus(); providerKeyVisible.CaretIndex = providerKeyVisible.Text.Length;
        }
        void HideProviderKey()
        {
            if (providerKeyVisible == null) return; keySyncing = true; providerKeyVisible.Visibility = Visibility.Collapsed; providerKeyVisible.Clear(); providerKey.Visibility = Visibility.Visible; keySyncing = false; L.Bind(providerKeyToggle, ContentControl.ContentProperty, L.M("workspace.showKey"));
        }
        async Task ClearProviderKey()
        {
            HideProviderKey(); if (!Confirm(L.M("workspace.clearConfirm"))) return;
            if (editingProvider != null) await config.ClearProviderKeyAsync(paths.OcxConfig, editingProvider);
            populating = true; providerKey.Clear(); populating = false; keyEdited = false; Invalidate(); RememberDraft();
        }
        void UpdateWorkspaceState()
        {
            if (!workspaceReady || populating) return;
            var name = String.IsNullOrWhiteSpace(providerName.Text) ? providerId.Text : providerName.Text;
            SetText(providerHeading, String.IsNullOrWhiteSpace(name) ? L.M("workspace.newProvider") : L.Raw(name));
            SetText(providerMeta, L.Raw(providerId.Text + "  ·  " + Convert.ToString(adapterBox.SelectedItem)) + "  ·  " + L.M("workspace.unverified"));
            SetText(modelCounts, L.F("workspace.counts", choices.Count, choices.Count(x => x.Selected)));
            var state = L.M(fetchFailed ? "workspace.fetchFailed" : modelStale ? "workspace.stale" : choices.Count == 0 ? "workspace.noModels" : "workspace.cached");
            DateTime parsed; var time = DateTime.TryParse(lastFetch, out parsed) ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
            SetText(providerStatus, state + "\n" + L.F("workspace.fetchedAt", time));
            SetText(modelWorkspaceHeading, String.IsNullOrWhiteSpace(name) ? L.M("workspace.chooseProvider") : L.Raw(name));
            SetText(modelWorkspaceStatus, L.M(String.IsNullOrWhiteSpace(providerKey.Password) ? "workspace.keyMissing" : "workspace.keyPresent") + "  ·  " + state + "  ·  " + L.F("workspace.counts", choices.Count, choices.Count(x => x.Selected)) + (selectionDirty ? L.M("workspace.unsaved") : L.Raw("")));
            if (modelEmpty != null) { modelEmpty.Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed; SetText(modelEmpty, L.M("workspace.noModels")); }
        }
        UIElement ModelWorkspace()
        {
            var panel = new StackPanel(); panel.Children.Add(SectionHeader(L.M("workspace.models"), L.M("workspace.modelIntro")));
            var head = new StackPanel(); modelProviderBox = ProviderSelector(); head.Children.Add(modelProviderBox);
            modelWorkspaceHeading = Text("", 23); modelWorkspaceStatus = Text(""); head.Children.Add(modelWorkspaceStatus);
            var buttons = new WrapPanel(); buttons.Children.Add(AsyncBtn(L.M("text.028"), FetchModels)); buttons.Children.Add(Btn(L.M("models.configure"), () => Navigate("providers"), true)); head.Children.Add(buttons); panel.Children.Add(Card(head));
            var body = new StackPanel(); search = Field(body, L.M("text.030")); search.TextChanged += delegate { ApplyModelFilter(); };
            var filters = new WrapPanel(); allModelsButton = Btn(L.M("workspace.all"), () => { onlySelected = false; ApplyModelFilter(); }, true); selectedModelsButton = Btn(L.M("workspace.selected"), () => { onlySelected = true; ApplyModelFilter(); }, true); filters.Children.Add(allModelsButton); filters.Children.Add(selectedModelsButton); body.Children.Add(filters);
            modelChoiceList = new ListBox { ItemsSource = choices, MinHeight = 200, MaxHeight = 420, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent }; ScrollViewer.SetHorizontalScrollBarVisibility(modelChoiceList, ScrollBarVisibility.Disabled);
            var template = new DataTemplate(typeof(ProviderModelChoice)); var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding("Selected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            var row = new FrameworkElementFactory(typeof(StackPanel)); var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("DisplayName")); name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.AppendChild(name);
            var route = new FrameworkElementFactory(typeof(TextBlock)); route.SetBinding(TextBlock.TextProperty, new Binding("Route")); route.SetValue(TextBlock.ForegroundProperty, muted); route.SetValue(TextBlock.FontSizeProperty, 12.0); route.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); row.AppendChild(route); check.AppendChild(row); template.VisualTree = check; modelChoiceList.ItemTemplate = template;
            modelChoiceList.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Space && !(e.OriginalSource is CheckBox)) { var selected = modelChoiceList.SelectedItem as ProviderModelChoice; if (selected != null) { selected.Selected = !selected.Selected; e.Handled = true; } } };
            body.Children.Add(modelChoiceList); modelEmpty = Text(L.M("workspace.noModels")); body.Children.Add(modelEmpty);
            var actions = new WrapPanel(); actions.Children.Add(Btn(L.M("text.031"), () => SetVisibleSelection(true), true)); actions.Children.Add(Btn(L.M("text.032"), () => SetVisibleSelection(false), true)); actions.Children.Add(AsyncBtn(L.M("text.033"), () => Import(false))); actions.Children.Add(AsyncBtn(L.M("text.034"), () => Import(true))); body.Children.Add(actions); panel.Children.Add(Card(body));
            modelProviderBox.SelectionChanged += delegate { if (!populating) SwitchProvider(modelProviderBox.SelectedItem as ProviderOption); };
            RefreshProviderSelectors(editingProvider); UpdateWorkspaceState(); ApplyModelFilter(); return panel;
        }
        void SetVisibleSelection(bool value) { foreach (var row in CollectionViewSource.GetDefaultView(choices).Cast<ProviderModelChoice>().ToList()) row.Selected = value; selectionDirty = true; UpdateWorkspaceState(); ApplyModelFilter(); }
        void ApplyModelFilter()
        {
            if (search == null || populating) return; var query = search.Text.Trim();
            CollectionViewSource.GetDefaultView(choices).Filter = obj => { var row = (ProviderModelChoice)obj; return (!onlySelected || row.Selected) && (row.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || row.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0); };
            if (allModelsButton != null) { Secondary(allModelsButton); Secondary(selectedModelsButton); ModernTheme.Primary(onlySelected ? selectedModelsButton : allModelsButton); }
            if (modelEmpty != null) { bool empty = CollectionViewSource.GetDefaultView(choices).IsEmpty; modelEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed; SetText(modelEmpty, L.M(choices.Count == 0 ? "workspace.noModels" : "workspace.noMatches")); }
        }
    }
}
