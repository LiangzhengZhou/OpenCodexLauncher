using System;
using System.IO;
using System.Linq;

namespace OpenCodexLauncherV2
{
    // Only maintainer-confirmed releases belong here. Pin the actual published ZIP,
    // not a mutable latest tag. Return fresh objects so callers cannot alter policy.
    public static class CriticalVersions
    {
        public static LauncherRelease[] All()
        {
            return new[] { new LauncherRelease { Version="2.6.5", Size=850118,
                Sha256="700a5eb7514079dfeefc3671bcd81386022bfb7d7c42795e365654face7dc566",
                Url="https://github.com/LiangzhengZhou/OpenCodexLauncher/releases/download/v2.6.5/OpenCodexLauncher-2.6.5-windows-x64.zip" } };
        }
        public static LauncherRelease Find(string version)
        {
            var entry=All().SingleOrDefault(x=>x.Version==version);
            if(entry==null)throw new InvalidDataException(L.M("rollback.unconfirmed"));
            return entry;
        }
        public static void Validate(LauncherRelease release, string current)
        {
            LauncherUpdater.ValidateRelease(release);
            var expected=Find(release.Version);
            if(release.Url!=expected.Url || release.Sha256!=expected.Sha256 || release.Size!=expected.Size ||
                LauncherUpdater.ParseVersion(release.Version)>=LauncherUpdater.ParseVersion(current))
                throw new InvalidDataException(L.M("rollback.unconfirmed"));
        }
        public static void CheckConfiguration()
        {
            // Reading does not migrate or rewrite the on-disk settings. Version 2.6.5
            // supports schema 1, the same provider storage and DPAPI credential store.
            var settings=PathResolver.Load();
            if(settings.SettingsVersion!=1)throw new InvalidDataException(L.M("rollback.incompatible"));
            if(settings.ReserveForceEnabled || settings.ReserveForceOwned)
                throw new InvalidOperationException(L.M("install.reserve"));
            if(settings.SetupCompleted)
            {
                var paths=PathResolver.Resolve(settings);
                SetupService.Validate(paths);
                if(!String.IsNullOrEmpty(new ConfigStore().ReserveForceTargetRoute(paths.OcxConfig)))
                    throw new InvalidOperationException(L.M("install.reserve"));
            }
        }
        public static string CachePath(LauncherRelease release)
        {
            Validate(release, LauncherUpdater.CurrentVersion);
            return Path.Combine(LocalEnvironment.Current.DataDirectory,"updates","critical-cache",release.Version+"-"+release.Sha256+".zip");
        }
        public static bool HasCache(LauncherRelease release)
        {
            var path=CachePath(release);LauncherUpdater.NoLinks(path);
            if(!File.Exists(path))return false;
            if(new FileInfo(path).Length!=release.Size || LauncherUpdater.FileHash(path)!=release.Sha256)
                throw new InvalidDataException(L.M("rollback.cacheInvalid"));
            return true;
        }
    }
}
