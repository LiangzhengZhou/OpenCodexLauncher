using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        public Func<LauncherUpdater> LauncherUpdaterFactory = () => new LauncherUpdater();
        UIElement LauncherUpdatePanel()
        {
            var panel=new StackPanel();panel.Children.Add(Text(L.M("update.title"),18));
            panel.Children.Add(Text(L.F("update.description",LauncherUpdater.CurrentVersion)));
            var state=Text(L.M("update.idle"));panel.Children.Add(state);
            LauncherRelease release=null;CancellationTokenSource updateCancellation=null;
            Button install=null;var actions=new WrapPanel();
            actions.Children.Add(AsyncBtn(L.M("update.check"),async ()=> {
                release=null;install.IsEnabled=false;SetText(state,L.M("update.checking"));
                try {release=await LauncherUpdaterFactory().CheckAsync(life.Token);SetText(state,release==null?L.M("update.latest"):L.F("update.available",release.Version));install.IsEnabled=release!=null;}
                catch {SetText(state,L.M("update.failed"));throw;}
            }));
            install=AsyncBtn(L.M("update.install"),async ()=> {
                if(release==null || !Confirm(L.F("update.confirm",release.Version)))return;
                install.IsEnabled=false;
                using(updateCancellation=CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                {
                    try
                    {
                        SetText(state,L.M("update.downloading"));
                        var plan=await LauncherUpdaterFactory().StageAsync(release,AppDomain.CurrentDomain.BaseDirectory,updateCancellation.Token);
                        updateCancellation.Token.ThrowIfCancellationRequested();
                        SetText(state,L.M("update.restarting"));
                        using(var helper=await LauncherUpdater.StartHelperAsync(plan,updateCancellation.Token))
                        { Close(); if(IsVisible)File.WriteAllText(Path.Combine(Path.GetDirectoryName(plan),"cancel"),""); }
                    }
                    catch(OperationCanceledException){SetText(state,L.M("update.cancelled"));throw;}
                    catch {SetText(state,L.M("update.failed"));throw;}
                    finally {updateCancellation=null;install.IsEnabled=true;}
                }
            });
            install.IsEnabled=false;actions.Children.Add(install);
            actions.Children.Add(Btn(L.M("update.cancel"),()=>{if(updateCancellation!=null)updateCancellation.Cancel();},true));
            panel.Children.Add(actions);return Card(panel);
        }
    }
}
