using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using OpenCodexLauncherV2;

partial class UiChecks
{
    static object Invoke(object target,string name,params object[] args) { return target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,args); }
    static void Await(Task task) { Until(()=>task.IsCompleted);task.GetAwaiter().GetResult(); }
    static void WorkspaceChecks(string output)
    {
        LocalEnvironment.UseIsolated(Path.Combine(output,"workspace-fixture"));
        var w=new MainWindow();w.Show();L.SetLanguage("en");Pump();
        Click(Find<Button>(w).Single(b=>Convert.ToString(b.Content)=="Manual setup"));
        var store=Field<ConfigStore>(w,"config");var path=Field<PathSet>(w,"paths").OcxConfig;
        var p=new ProviderOption {Id="demo-provider",DisplayName="Demo Provider",BaseUrl="https://example.invalid/v1",Adapter="openai-responses"};
        var q=new ProviderOption {Id="sample-provider",DisplayName="Sample Provider",BaseUrl="https://example.invalid/v1",Adapter="openai-chat"};
        Await(store.UpsertProviderAsync(path,p,"dummy-workspace-alpha"));Await(store.UpsertProviderAsync(path,q,"dummy-workspace-beta"));
        Await(store.SelectProviderModelsAsync(path,p.Id,p.DisplayName,new[]{"shared-model"},new[]{"shared-model","alpha-model"}));
        Await(store.SelectProviderModelsAsync(path,q.Id,q.DisplayName,new[]{"shared-model"},new[]{"shared-model","beta-model"}));
        Invoke(w,"Populate",p);var nav=Field<ListBox>(w,"navigation");nav.SelectedIndex=1;Pump();w.UpdateLayout();
        var box=Field<ComboBox>(w,"providerBox");
        Check(box.SelectedItem!=null&&Find<TextBlock>(box).Any(t=>t.Text==p.ToString()),"closed provider selector renders selected provider name");
        Check(!Find<TextBlock>(w).Any(t=>t.Text.Contains("Default model ID")),"provider editor has no default-model field");
        var key=Field<PasswordBox>(w,"providerKey");var plain=Field<TextBox>(w,"providerKeyVisible");
        Check(key.Password=="dummy-workspace-alpha"&&plain.Visibility==Visibility.Collapsed&&plain.Text=="","saved key loads masked with no plaintext textbox content");
        Invoke(w,"ToggleProviderKey");Pump();Check(plain.Text==key.Password&&plain.Visibility==Visibility.Visible,"show key reveals saved value");
        Check(plain.Focus()&&plain.IsKeyboardFocused&&!plain.IsReadOnly,"visible key field accepts keyboard focus and edits");
        plain.SelectAll();TextCompositionManager.StartComposition(new TextComposition(InputManager.Current,plain,"dummy-workspace-edited"));Pump();
        Check(key.Password=="dummy-workspace-edited"&&plain.Text==key.Password,"keyboard text composition edits and synchronizes both key controls");
        Await((Task)Invoke(w,"SaveProvider"));Check(CredentialStore.ForProvider(path,p.Id)=="dummy-workspace-edited","edited key saves through encrypted credential storage");
        var snapshot=CredentialStore.Snapshot(p.Id);Await((Task)Invoke(w,"SaveProvider"));
        Check(snapshot.SequenceEqual(CredentialStore.Snapshot(p.Id)),"unchanged key preserves exact DPAPI bytes");
        plain.Clear();Await((Task)Invoke(w,"SaveProvider"));Check(key.Password=="dummy-workspace-edited"&&plain.Text==key.Password,"empty edit preserves and redisplays saved key until explicit clear");
        var input=Field<TextBox>(w,"providerName");input.Focus();Check(input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),"provider inputs support Tab focus traversal");
        var rows=Field<ObservableCollection<ProviderModelChoice>>(w,"choices");rows.Single(r=>r.Id=="alpha-model").Selected=true;
        input.Text="Demo draft name";Field<TextBox>(w,"providerUrl").Text="https://example.invalid/changed";
        Check(rows.Count==2&&rows.All(r=>r.Selected)&&Field<bool>(w,"modelStale"),"connection edits mark stale without losing cached rows or checks");
        Invoke(w,"SwitchProvider",q);Check(key.Password=="dummy-workspace-beta"&&plain.Visibility==Visibility.Collapsed&&rows.Any(r=>r.Id=="beta-model")&&!rows.Any(r=>r.Id=="alpha-model"),"provider switch hides key and isolates models and credentials");
        Invoke(w,"SwitchProvider",p);Check(input.Text=="Demo draft name"&&rows.Count==2&&rows.All(r=>r.Selected),"switching back restores provider draft and unsaved selection");
        Invoke(w,"ToggleProviderKey");nav.SelectedIndex=2;Pump();Check(plain.Visibility==Visibility.Collapsed&&plain.Text.Length==0,"leaving provider page clears visible plaintext");
        var modelList=Field<ListBox>(w,"modelChoiceList");Check(modelList.Items.Count==2&&rows.All(r=>r.Route.StartsWith(p.Id+"/")),"model workspace shows complete current-provider route list");
        Field<TextBox>(w,"search").Text="alpha";Pump();Check(modelList.Items.Count==1,"model search matches IDs");
        Invoke(w,"SetVisibleSelection",false);Check(!rows.Single(r=>r.Id=="alpha-model").Selected&&rows.Single(r=>r.Id=="shared-model").Selected,"filtered deselect changes visible rows only");
        Field<TextBox>(w,"search").Clear();Click(Field<Button>(w,"selectedModelsButton"));Check(modelList.Items.Count==1,"selected-only filter excludes unchecked models");
        L.SetLanguage("zh");Pump();Check(modelList.Items.Count==1&&Field<bool>(w,"onlySelected")&&rows.Count==2&&input.Text=="Demo draft name","language switch keeps provider draft, filter and row identities");
        L.SetLanguage("en");Click(Field<Button>(w,"allModelsButton"));Invoke(w,"SetVisibleSelection",true);Await((Task)Invoke(w,"Import",false));
        Check(store.SelectedModels(path,p.Id).SetEquals(new[]{"shared-model","alpha-model"})&&store.SelectedModels(path,q.Id).SetEquals(new[]{"shared-model"}),"saving current-provider choices preserves other provider same-name route");
        w.ModelFetch=(provider,secret,token)=>Task.FromResult(new List<string>{"shared-model","new-model"});
        Await((Task)Invoke(w,"FetchModels"));Check(rows.Count==3&&rows.Count(r=>r.Selected)==2&&!Field<bool>(w,"modelStale")&&!String.IsNullOrEmpty(Field<string>(w,"lastFetch")),"successful fetch merges old choices and records timestamp without inference");
        w.ModelFetch=(provider,secret,token)=>{throw new IOException("fixture fetch failed");};
        var failed=(Task)Invoke(w,"FetchModels");Until(()=>failed.IsCompleted);
        Check(failed.IsFaulted&&rows.Count==3&&rows.Count(r=>r.Selected)==2&&Field<bool>(w,"fetchFailed"),"failed fetch retains complete cached list and selection");
        Check(!Redactor.Apply("fixture dummy-workspace-edited failure").Contains("dummy-workspace-edited"),"exact revealed/edited key is redacted from arbitrary errors");
        nav.SelectedIndex=1;Invoke(w,"ToggleProviderKey");var otherWindow=new Window{Width=200,Height=150,Owner=w,ShowInTaskbar=false};otherWindow.Show();otherWindow.Activate();Pump();
        Check(plain.Visibility==Visibility.Collapsed,"window deactivation automatically masks key");otherWindow.Close();
        // The modal clear flow is exercised with cancellation followed by explicit confirmation.
        w.Dispatcher.BeginInvoke(new Action(()=>{var dialog=w.OwnedWindows.Cast<Window>().Single();Click(Find<Button>(dialog).Single(b=>b.IsCancel));}));
        Await((Task)Invoke(w,"ClearProviderKey"));Check(CredentialStore.ForProvider(path,p.Id)=="dummy-workspace-edited","cancelled explicit clear leaves stored key intact");
        w.Dispatcher.BeginInvoke(new Action(()=>{var dialog=w.OwnedWindows.Cast<Window>().Single();Render(dialog,Path.Combine(output,"modern-confirm.png"),1);Click(Find<Button>(dialog).Single(b=>b.IsDefault));}));
        Await((Task)Invoke(w,"ClearProviderKey"));Check(CredentialStore.ForProvider(path,p.Id)==""&&key.Password=="","confirmed explicit clear removes effective key");
        Check(File.Exists(CredentialStore.PathFor(p.Id)),"clear records encrypted empty value to prevent ambient key resurrection");
        // Render a realistic populated workspace using only fictitious provider data.
        Invoke(w,"SwitchProvider",q);Invoke(w,"SwitchProvider",p);
        foreach(var language in new[]{"en","zh"}) { L.SetLanguage(language);foreach(int index in new[]{1,2}) {nav.SelectedIndex=index;Pump();foreach(var width in new[]{980.0,1320.0}) {w.Width=width;w.Height=900;Pump();foreach(var scale in new[]{1.0,1.5,2.0})Render(w,Path.Combine(output,language+"-workspace-"+index+"-"+width+"-"+(int)(scale*100)+".png"),scale);Check(Find<ScrollViewer>(Field<ContentControl>(w,"content")).All(s=>s.ExtentWidth<=s.ViewportWidth+1),language+" populated workspace "+index+" fits width "+width);}}}
        w.Close();Pump();
    }
}
