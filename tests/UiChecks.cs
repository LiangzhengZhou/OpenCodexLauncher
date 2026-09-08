using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCodexLauncherV2;

class UiChecks
{
    static int passed;
    static void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);passed++;}
    static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(obj);}
    static IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T)yield return (T)child;foreach(var item in Find<T>(child))yield return item;}}
    static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(()=>{}));}
    static void Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
    static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(10);while(!done()){if(DateTime.UtcNow>end)throw new Exception("UI operation timed out");Pump();System.Threading.Thread.Sleep(10);}Pump();}
    static void Render(MainWindow w,string file,double scale)
    {
        w.UpdateLayout();var visual=(FrameworkElement)w.Content;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*scale),(int)Math.Ceiling(visual.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
        bitmap.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(file))png.Save(stream);
    }
    [STAThread]static int Main(string[] args)
    {
        try
        {
            var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
            LocalEnvironment.UseIsolated(Path.Combine(output,"environment"));
            // Deliberately invalid ambient files prove onboarding does not open them.
            var ambient=PathResolver.Resolve(new LauncherSettings());
            Directory.CreateDirectory(Path.GetDirectoryName(ambient.OcxConfig));File.WriteAllText(ambient.OcxConfig,"invalid ambient provider config");
            Directory.CreateDirectory(ambient.CodexHome);File.WriteAllText(ambient.Catalog,"invalid ambient catalog");
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var w=new MainWindow {Width=980,Height=700};w.Show();Pump();
            Check(!Field<LauncherSettings>(w,"settings").SetupCompleted && Field<PathSet>(w,"paths").OcxConfig==null,"fresh UI ignores existing malformed ambient files");
            Check(Field<Dictionary<string,UIElement>>(w,"pages").Count==0,"fresh UI does not build provider or model pages");
            L.SetLanguage("en");Pump();Render(w,Path.Combine(output,"setup-en.png"),1);
            int updateRequests=0;
            w.LauncherUpdaterFactory=()=>new LauncherUpdater((uri,limit,token)=> {updateRequests++;return Task.FromResult(System.Text.Encoding.UTF8.GetBytes("{\"tag_name\":\"v"+LauncherUpdater.CurrentVersion+"\",\"draft\":false,\"prerelease\":false}"));});
            Check(updateRequests==0 && Find<Button>(w).Any(b=>Convert.ToString(b.Content)=="Check launcher updates"),"launcher updater is available before setup without automatic network requests");
            Click(Find<Button>(w).Single(b=>Convert.ToString(b.Content)=="Check launcher updates"));
            Until(()=>Find<TextBlock>(w).Any(b=>b.Text=="Already on the latest stable version."));
            Check(updateRequests==1 && !Field<LauncherSettings>(w,"settings").SetupCompleted && Field<PathSet>(w,"paths").OcxConfig==null,"launcher update check leaves onboarding and private configuration unlinked");
            L.SetLanguage("zh");Pump();
            Check(Find<TextBlock>(w).Any(b=>b.Text=="已是最新稳定版。"),"launcher update status changes language without repeating network requests");
            L.SetLanguage("en");Pump();
            Click(Find<Button>(w).Single(b=>Convert.ToString(b.Content)=="Manual setup"));
            Check(Field<LauncherSettings>(w,"settings").SetupCompleted && Field<LauncherSettings>(w,"settings").ConfigurationMode=="manual","manual onboarding completes in an empty independent environment");
            Check(Field<IList>(w,"models").Count==0 && Field<ComboBox>(w,"providerBox").Items.Count==0,"manual UI starts without providers or preset models");
            Check(Directory.Exists(Field<PathSet>(w,"paths").CodexHome),"manual onboarding creates the empty Codex home before returning to the UI");
            var nav=Field<ListBox>(w,"navigation");nav.SelectedIndex=1;Pump();
            var id=Field<TextBox>(w,"providerId");var key=Field<PasswordBox>(w,"providerKey");id.Text="draft-provider";key.Password="dummy-draft-key";
            var choices=Field<System.Collections.ObjectModel.ObservableCollection<ProviderModelChoice>>(w,"choices");var choice=new ProviderModelChoice{Id="draft-model",DisplayName="draft-model",Selected=true};choices.Add(choice);
            var englishTexts=Find<TextBlock>(w).Select(b=>b.Text).ToArray();
            Click(Find<Button>(w).Single(b=>Convert.ToString(b.Content)=="中文 / EN"));
            Check(Field<TextBox>(w,"providerId")==id && id.Text=="draft-provider" && key.Password=="dummy-draft-key","language switch preserves textbox and password drafts in the same controls");
            Check((string)((ListBoxItem)nav.SelectedItem).Tag=="providers" && choice.Selected && choices.Contains(choice),"language switch preserves page and selected model objects");
            Check(L.Language=="zh" && PathResolver.Load().Language=="zh","language button persists the chosen language");
            Check(Find<TextBlock>(w).Any(b=>b.Text.Contains("供应商 ID")),"existing labels update immediately in Chinese");
            Click(Find<Button>(w).Single(b=>Convert.ToString(b.Content)=="中文 / EN"));
            Check(Find<TextBlock>(w).Any(b=>b.Text.Contains("Provider ID")),"existing labels update immediately in English");
            // Exercise the real provider editor and selection-save button with no runtime.
            id.Text="demo-provider";Field<TextBox>(w,"providerName").Text="Demo Provider";
            Field<TextBox>(w,"providerUrl").Text="https://example.invalid/v1";
            Click(Find<Button>(w).Single(b=>Convert.ToString(b.Content)=="Save provider"));
            Until(()=>Field<SemaphoreSlim>(w,"gate").CurrentCount==1);
            var provider=Field<ConfigStore>(w,"config").Providers(Field<PathSet>(w,"paths").OcxConfig).Single();
            w.GetType().GetMethod("Populate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,new object[]{provider});
            choices.Add(new ProviderModelChoice{Id="fixture-model",DisplayName="Demo Provider Fixture Model",Selected=false});
            w.UpdateLayout();Pump();
            Find<CheckBox>(w).Single(c=>c.DataContext==choices[0]).IsChecked=true;Pump();
            Check(choices[0].Selected,"real provider checkbox binding updates the model selection");
            w.GetType().GetField("fetchedFor",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(w,
                w.GetType().GetMethod("Fingerprint",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,new object[]{provider}));
            Click(Find<Button>(w).Single(b=>Convert.ToString(b.Content)=="Save selection"));
            Until(()=>Field<SemaphoreSlim>(w,"gate").CurrentCount==1);
            nav.SelectedIndex=2;Pump();
            Check(Field<ComboBox>(w,"modelBox").Items.Cast<ModelOption>().Any(m=>m.Id=="demo-provider/fixture-model"&&m.DisplayName.StartsWith("Demo Provider")),"saving selection immediately populates Models without Codex, catalog, proxy or sync");
            Check(Field<TextBlock>(w,"providerStatus").Text.Contains("Saved and verified 1")&&Field<TextBox>(w,"logs").Text.Contains("Saved and verified 1"),"model save confirmation survives provider repopulation and records a verified count");
            var modelPaths=Field<PathSet>(w,"paths");var store=Field<ConfigStore>(w,"config");
            nav.SelectedIndex=1;Pump();
            store.AddCustomModelsAsync(modelPaths.OcxConfig,"demo-provider",new[]{"second-model"}).GetAwaiter().GetResult();
            nav.SelectedIndex=2;Pump();
            Check(Field<ComboBox>(w,"modelBox").Items.Cast<ModelOption>().Any(m=>m.Id=="demo-provider/second-model"),"entering Models reloads externally updated selections without CLI or sync");
            File.WriteAllText(modelPaths.Catalog,"{incomplete");var damagedHash=FileTransaction.Hash(modelPaths.Catalog);
            nav.SelectedIndex=1;nav.SelectedIndex=2;Pump();
            Check(Field<ComboBox>(w,"modelBox").Items.Count==2&&Field<TextBlock>(w,"modelSummary").Text.Contains("unreadable"),"unreadable generated catalog reports a warning without hiding local models");
            var fakeCli=Path.Combine(output,"invalid-model-output.cmd");File.WriteAllText(fakeCli,"@echo off\r\necho invalid-json\r\nexit /b 0\r\n");modelPaths.Codex=fakeCli;
            var refresh=(Task)w.GetType().GetMethod("RefreshModels",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(w,null);Until(()=>refresh.IsCompleted);refresh.GetAwaiter().GetResult();
            Check(Field<ComboBox>(w,"modelBox").Items.Count==2&&Field<TextBlock>(w,"modelSummary").Text.Contains("CLI catalog refresh failed"),"malformed CLI output preserves saved third-party models and reports the refresh failure");
            Check(FileTransaction.Hash(modelPaths.Catalog)==damagedHash,"UI fallback leaves unreadable generated catalog untouched");
            modelPaths.Codex=null;
            var diagnostic=ModelDiagnostics.Create(modelPaths,Field<LauncherSettings>(w,"settings"),Field<System.Collections.ObjectModel.ObservableCollection<ModelOption>>(w,"models"),Field<string>(w,"nativeRefreshState"));
            Check(diagnostic.Contains("\"visibleThirdPartyModels\":2")&&!diagnostic.Contains("demo-provider")&&!diagnostic.Contains(output),"UI diagnostics expose counts and failure states without private provider names or paths");
            nav.SelectedIndex=1;nav.SelectedIndex=2;Pump();
            var savedSelection=(ModelOption)Field<ComboBox>(w,"modelBox").Items[1];Field<ComboBox>(w,"modelBox").SelectedItem=savedSelection;
            L.SetLanguage("zh");Pump();
            Check(Field<ComboBox>(w,"modelBox").SelectedItem==savedSelection&&Field<TextBlock>(w,"modelSummary").Text.Contains("第三方模型 2"),"model summary switches language while retaining the selected route");
            L.SetLanguage("en");
            foreach(var language in new[]{"en","zh"})
            {
                L.SetLanguage(language);Pump();
                for(int index=0;index<nav.Items.Count;index++)
                {
                    nav.SelectedIndex=index;Pump();w.UpdateLayout();
                    foreach(var scale in new[]{1.0,1.5,2.0})Render(w,Path.Combine(output,language+"-"+((ListBoxItem)nav.Items[index]).Tag+"-"+(int)(scale*100)+".png"),scale);
                    Check(Find<ScrollViewer>(Field<ContentControl>(w,"content")).All(s=>s.ExtentWidth<=s.ViewportWidth+1),language+" page "+index+" fits minimum width");
                }
            }
            w.Close();Pump();
            var reopened=new MainWindow();reopened.Show();Pump();
            Check(!Field<bool>(reopened,"recoveryMode")&&Field<ComboBox>(reopened,"modelBox").Items.Count==2,"reopening with an unreadable optional catalog preserves saved third-party models without blocking startup");
            var desktopHome=Path.Combine(output,"desktop-ui");Directory.CreateDirectory(desktopHome);
            var desktopConfig=Path.Combine(desktopHome,"config.toml");File.WriteAllText(desktopConfig,"# desktop fixture\n");
            var providerHash=FileTransaction.Hash(Field<PathSet>(reopened,"paths").OcxConfig);
            var linked=DesktopAssociation.Preview(Field<LauncherSettings>(reopened,"settings"),desktopConfig);
            reopened.GetType().GetMethod("ApplyDesktopAssociation",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(reopened,new object[]{linked});
            Check(Field<PathSet>(reopened,"paths").CodexConfig==desktopConfig&&PathResolver.Load().DesktopConfigPath==desktopConfig,"Desktop association updates UI target and persists only after apply");
            Check(FileTransaction.Hash(Field<PathSet>(reopened,"paths").OcxConfig)==providerHash&&Field<ComboBox>(reopened,"modelBox").Items.Count==2,"Desktop association keeps saved providers and third-party model list");
            Check(Field<List<ModelOption>>(reopened,"native").Count==0&&Field<List<ModelOption>>(reopened,"catalogCache").Count==0,"Desktop association clears old-home catalog caches");
            Check(File.ReadAllText(desktopConfig)=="# desktop fixture\n"&&Directory.GetFiles(desktopHome).Length==1,"UI association does not sync, import auth or start services");
            var routedNav=Field<ListBox>(reopened,"navigation");routedNav.SelectedIndex=4;L.SetLanguage("en");Pump();
            Check(Find<Button>(reopened).Any(b=>Convert.ToString(b.Content)=="Associate Desktop config…")&&Field<TextBlock>(reopened,"desktopTarget").Text.Contains(desktopConfig),"routing page exposes selected Desktop target and explicit association button");
            foreach(var scale in new[]{1.0,1.5,2.0})Render(reopened,Path.Combine(output,"en-desktop-associated-"+(int)(scale*100)+".png"),scale);
            L.SetLanguage("zh");Pump();
            Check(Find<Button>(reopened).Any(b=>Convert.ToString(b.Content)=="关联 Desktop 配置…")&&Field<TextBlock>(reopened,"desktopTarget").Text.Contains(desktopConfig),"Desktop association labels switch language without changing selected path");
            foreach(var scale in new[]{1.0,1.5,2.0})Render(reopened,Path.Combine(output,"zh-desktop-associated-"+(int)(scale*100)+".png"),scale);
            reopened.Close();Pump();
            var linkedReopen=new MainWindow();linkedReopen.Show();Pump();
            Check(Field<PathSet>(linkedReopen,"paths").CodexConfig==desktopConfig&&Field<ComboBox>(linkedReopen,"modelBox").Items.Count==2,"reopening retains Desktop target and manual providers");linkedReopen.Close();Pump();
            File.WriteAllText(PathResolver.SettingsPath(),"{invalid");var hash=FileTransaction.Hash(PathResolver.SettingsPath());
            var recovery=new MainWindow();recovery.Show();Pump();
            Check(Field<bool>(recovery,"recoveryMode") && FileTransaction.Hash(PathResolver.SettingsPath())==hash,"corrupt settings show recovery UI without overwriting original bytes");
            Click(Find<Button>(recovery).Single(b=>Convert.ToString(b.Content)=="中文 / EN"));
            Check(FileTransaction.Hash(PathResolver.SettingsPath())==hash,"language switching in recovery never overwrites damaged settings");
            Render(recovery,Path.Combine(output,"recovery.png"),1);recovery.Close();
            LocalEnvironment.UseIsolated(Path.Combine(output,"one-click"));
            var install=new MainWindow {Width=980,Height=700};var fake=new FakeInstallerPlatform();
            install.InstallerFactory=()=>new OpenCodexInstaller(Path.Combine(LocalEnvironment.Current.DataDirectory,"runtimes"),fake);
            install.Show();L.SetLanguage("en");Pump();
            Check(fake.Calls==0,"one-click UI makes no network requests before an explicit click");
            Click(Find<Button>(install).Single(b=>Convert.ToString(b.Content)=="Install and start setup"));
            Until(()=>Field<LauncherSettings>(install,"settings").SetupCompleted);
            Check(PathResolver.Load().ConfigurationMode=="manual"&&File.Exists(PathResolver.Load().OcxPath),"one-click UI saves the verified runtime and completes empty onboarding");
            Check(Field<IList>(install,"models").Count==0&&Field<ComboBox>(install,"providerBox").Items.Count==0,"one-click installation does not populate providers or models");
            Check(Directory.Exists(Field<PathSet>(install,"paths").CodexHome),"one-click onboarding creates the empty Codex home before publishing selection");
            var installNav=Field<ListBox>(install,"navigation");installNav.SelectedIndex=6;Pump();
            Click(Find<Button>(install).Single(b=>Convert.ToString(b.Content)=="Check for updates"));
            Until(()=>Find<TextBlock>(install).Any(b=>b.Text.Contains("current or newer")));
            Check(Find<TextBlock>(install).Any(b=>b.Text.Contains("3.2.1")),"update check shows installed and latest versions without invoking CLI");
            L.SetLanguage("zh");Pump();
            Check(Find<Button>(install).Any(b=>Convert.ToString(b.Content)=="检查更新")&&Find<TextBlock>(install).Any(b=>b.Text.Contains("当前版本已是最新")),"installer controls and completed status switch languages live");
            Render(install,Path.Combine(output,"installer-zh.png"),1);install.Close();
            LocalEnvironment.UseIsolated(Path.Combine(output,"cancel-install"));
            var cancelWindow=new MainWindow {Width=980,Height=700};var paused=new FakeInstallerPlatform {PauseDownload=new TaskCompletionSource<bool>()};
            cancelWindow.InstallerFactory=()=>new OpenCodexInstaller(Path.Combine(LocalEnvironment.Current.DataDirectory,"runtimes"),paused);
            cancelWindow.Show();L.SetLanguage("en");Pump();
            Click(Find<Button>(cancelWindow).Single(b=>Convert.ToString(b.Content)=="Install and start setup"));
            Check(!Find<Button>(cancelWindow).Single(b=>Convert.ToString(b.Content)=="Manual setup").IsEnabled,"onboarding cannot change configuration during installation");
            Click(Find<Button>(cancelWindow).Single(b=>Convert.ToString(b.Content)=="Cancel install / check"));paused.PauseDownload.SetResult(true);
            Until(()=>Find<TextBlock>(cancelWindow).Any(b=>b.Text.StartsWith("Cancelled.")));
            Check(!PathResolver.Load().SetupCompleted&&Field<PathSet>(cancelWindow,"paths").OcxConfig==null,"cancel button leaves onboarding and account paths unlinked");
            Check(!Find<Button>(cancelWindow).Single(b=>Convert.ToString(b.Content)=="Confirm import").IsEnabled,"cancel restores the original disabled import confirmation");
            cancelWindow.Close();app.Shutdown();
            Console.WriteLine("ALL "+passed+" UI CHECKS PASSED; 42 page/scale renders generated");return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
