using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.IO;
using System.Threading;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        UIElement VersionManagementPanel()
        {
            var panel = new StackPanel();
            panel.Children.Add(Text(L.M("version.title"), 18));
            panel.Children.Add(Text(L.M("version.description"), 13));
            panel.Children.Add(Text(L.Raw("Launcher " + LauncherUpdater.CurrentVersion), 14));
            var runtimeVersion=paths==null || String.IsNullOrEmpty(paths.Ocx) ? null : OpenCodexInstaller.ReadInstalledVersion(paths.Ocx);
            panel.Children.Add(Text(L.F("version.runtime",paths == null || String.IsNullOrEmpty(paths.Ocx) ? L.M("version.notInstalled") : runtimeVersion==null ? L.M("version.unknown") : L.Raw(runtimeVersion)), 14));
            panel.Children.Add(Text(L.M("rollback.description"),13));
            var versions=new ComboBox { Name="CriticalVersionSelector", MinWidth=240, HorizontalAlignment=HorizontalAlignment.Left, Margin=new Thickness(0,12,0,8) };
            versions.ItemsSource=CriticalVersions.All().Where(x=>LauncherUpdater.ParseVersion(x.Version)<LauncherUpdater.ParseVersion(LauncherUpdater.CurrentVersion)).Select(x=>x.Version).ToList();
            versions.SelectedIndex=0;panel.Children.Add(versions);
            var state=Text(L.M("rollback.idle"),13);panel.Children.Add(state);
            var actions = new WrapPanel();
            CancellationTokenSource cancellation=null;
            var rollback=AsyncBtn(L.M("rollback.action"),async ()=> {
                var version=versions.SelectedItem as string;if(version==null)return;
                var selected=CriticalVersions.Find(version);
                LauncherUpdater.ValidateTransition(selected,LauncherUpdater.CurrentVersion,true);
                var cached=CriticalVersions.HasCache(selected);
                if(!Confirm(L.F("rollback.confirm",LauncherUpdater.CurrentVersion,version)))return;
                versions.IsEnabled=false;
                using(cancellation=CancellationTokenSource.CreateLinkedTokenSource(life.Token))
                {
                    try
                    {
                        SetText(state,L.M(cached?"rollback.cached":"update.downloading"));
                        var plan=await LauncherUpdaterFactory().StageRollbackAsync(version,AppDomain.CurrentDomain.BaseDirectory,cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();SetText(state,L.M("update.restarting"));
                        using(var helper=await LauncherUpdater.StartHelperAsync(plan,cancellation.Token))
                        { Close();if(IsVisible)File.WriteAllText(Path.Combine(Path.GetDirectoryName(plan),"cancel"),""); }
                    }
                    catch(OperationCanceledException){SetText(state,L.M("update.cancelled"));throw;}
                    catch {SetText(state,L.M("rollback.failed"));throw;}
                    finally {cancellation=null;versions.IsEnabled=true;}
                }
            });
            rollback.Name="RollbackSelectedVersion";rollback.IsEnabled=versions.Items.Count>0;actions.Children.Add(rollback);
            actions.Children.Add(Btn(L.M("update.cancel"),()=>{if(cancellation!=null)cancellation.Cancel();},true));
            panel.Children.Add(actions);
            panel.Children.Add(Text(L.M("rollback.runtimePending"),12));
            return Card(panel);
        }
    }
}
