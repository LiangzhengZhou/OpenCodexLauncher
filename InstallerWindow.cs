using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        CancellationTokenSource installation;
        // The test seam permits exercising the actual UI without public network access.
        public Func<OpenCodexInstaller> InstallerFactory = () => new OpenCodexInstaller(
            Path.Combine(LocalEnvironment.Current.DataDirectory,"runtimes"),new InstallerPlatform());

        UIElement InstallerPanel(bool onboarding)
        {
            var panel=new StackPanel();panel.Children.Add(Text(L.M("install.title"),18));
            panel.Children.Add(Text(L.M(onboarding?"install.intro":"install.existing")));
            var state=Text(onboarding?L.M("install.idle"):InstalledVersionText());panel.Children.Add(state);
            var actions=new WrapPanel();var cancel=Btn(L.M("install.cancel"),()=>{if(installation!=null)installation.Cancel();});cancel.IsEnabled=false;
            Func<Func<OpenCodexInstaller,CancellationToken,Task>,Task> perform=async action=>{
                installation=CancellationTokenSource.CreateLinkedTokenSource(life.Token);installation.CancelAfter(TimeSpan.FromMinutes(20));
                cancel.IsEnabled=true;
                // Prevent choosing another onboarding path while installation is pending.
                var setup=onboarding?(StackPanel)((ScrollViewer)Content).Content:null;
                var enabled=new Dictionary<UIElement,bool>();
                if(setup!=null)foreach(UIElement child in setup.Children)if(child is Button){enabled[child]=child.IsEnabled;child.IsEnabled=false;}
                try{await action(InstallerFactory(),installation.Token);}
                catch(OperationCanceledException){SetText(state,L.M("install.cancelled"));}
                catch(Exception error){SetText(state,L.M("install.failed"));throw new IOException(L.M("install.failed")+"\n"+error.Message,error);}
                finally{
                    installation.Dispose();installation=null;cancel.IsEnabled=false;
                    foreach(var child in enabled)child.Key.IsEnabled=child.Value;
                }
            };
            if(!onboarding)actions.Children.Add(AsyncBtn(L.M("install.check"),()=>perform(async (service,token)=>{
                SetText(state,L.M("install.checking"));var release=await service.LatestAsync(token);
                SetText(state,InstalledVersionText()+"\n"+L.F("install.latest",release.Version)+"\n"+
                    L.M(release.IsNewerThan(OpenCodexInstaller.ReadInstalledVersion(paths.Ocx))?"install.available":"install.current"));
            })));
            actions.Children.Add(AsyncBtn(L.M(onboarding?"install.oneClick":"install.update"),()=>perform(async (service,token)=>{
                if(settings.ReserveForceEnabled)throw new InvalidOperationException(L.M("install.reserve"));
                SetText(state,L.M("install.checking"));var release=await service.LatestAsync(token);
                if(!onboarding && !release.IsNewerThan(OpenCodexInstaller.ReadInstalledVersion(paths.Ocx))){SetText(state,L.M("install.current"));return;}
                if(!onboarding && !Confirm(L.F("install.confirm",release.Version))){SetText(state,InstalledVersionText());return;}
                bool reporting=true;string entry;
                Action<string> progress=key=>{
                    if(!life.IsCancellationRequested)Dispatcher.BeginInvoke(new Action(()=>{if(reporting)SetText(state,L.M(key));}));
                };
                try {entry=await service.InstallAsync(release,progress,token);}
                finally {reporting=false;}
                token.ThrowIfCancellationRequested();
                var next=RuntimeSelection.Create(settings,paths.Ocx,entry,onboarding);
                var nextPaths=PathResolver.Resolve(next);SetupService.Validate(nextPaths);
                PathResolver.Save(next);settings=next;paths=nextPaths;
                SetText(state,L.F("install.done",release.Version));
                if(onboarding){Build();LoadModels();if(timer!=null)timer.Start();SetText(operation,L.F("install.done",release.Version));}
            })));
            if(!onboarding)actions.Children.Add(AsyncBtn(L.M("install.rollback"),async delegate {
                if(settings.ReserveForceEnabled)throw new InvalidOperationException(L.M("install.reserve"));
                if(!File.Exists(settings.PreviousOcxPath))throw new FileNotFoundException(L.M("install.noPrevious"));
                if(!Confirm(L.M("install.rollbackConfirm")))return;
                var next=RuntimeSelection.Create(settings,paths.Ocx,settings.PreviousOcxPath,false);
                var nextPaths=PathResolver.Resolve(next);SetupService.Validate(nextPaths);PathResolver.Save(next);settings=next;paths=nextPaths;
                SetText(state,InstalledVersionText()+"\n"+L.M("install.restart"));await Task.FromResult(0);
            }));
            actions.Children.Add(cancel);panel.Children.Add(actions);return panel;
        }
        LocalText InstalledVersionText(){var version=OpenCodexInstaller.ReadInstalledVersion(paths.Ocx);return L.M("install.installed")+(version==null?L.M("install.unknown"):L.Raw(version));}
    }

    public static class RuntimeSelection
    {
        public static LauncherSettings Create(LauncherSettings previous,string oldEntry,string newEntry,bool firstSetup)
        {
            if(previous.ReserveForceEnabled)throw new InvalidOperationException(L.M("install.reserve"));
            // Save the new settings before replacing the live in-memory reference.
            var next=JsonData.Serializer().Deserialize<LauncherSettings>(JsonData.Serializer().Serialize(previous));
            next.PreviousOcxPath=oldEntry;next.OcxPath=newEntry;
            if(firstSetup){next.ConfigurationMode="manual";next.SetupCompleted=true;}
            return next;
        }
    }
}
