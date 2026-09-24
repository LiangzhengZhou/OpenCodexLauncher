using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    public sealed class NativeActivationState
    {
        public string Helper { get; set; }
        public string Manifest { get; set; }
        public string PreviousCli { get; set; }
        public string PreviousBridge { get; set; }
        public string TransitionCli { get; set; }
        public string TransitionBridge { get; set; }
    }
    // Injectable environment access keeps regression tests out of the user's registry.
    public sealed class NativeActivation
    {
        const string Cli = "CODEX_CLI_PATH", Bridge = "OPENCODEX_LAUNCHER_BRIDGE";
        readonly string statePath;
        readonly Func<string, string> get;
        readonly Action<string, string> set;
        readonly Action broadcast;
        public NativeActivation(string path, Func<string,string> read, Action<string,string> write, Action notify)
        { statePath = path; get = read; set = write; broadcast = notify; }
        public static NativeActivation Current { get { return new NativeActivation(
            Path.Combine(LocalEnvironment.Current.DataDirectory, "native-activation.json"),
            key => Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User),
            (key,value) => Environment.SetEnvironmentVariable(key,value,EnvironmentVariableTarget.User), Broadcast); } }
        [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
        static void Broadcast() { UIntPtr result; SendMessageTimeout(new IntPtr(0xffff), 0x1a, UIntPtr.Zero, "Environment", 2, 3000, out result); }
        NativeActivationState Read() {
            if(!File.Exists(statePath)) return null;
            var state=JsonData.Serializer().Deserialize<NativeActivationState>(File.ReadAllText(statePath));
            if(state==null || String.IsNullOrWhiteSpace(state.Helper) || String.IsNullOrWhiteSpace(state.Manifest)) throw new InvalidDataException("Invalid Native activation recovery record.");
            return state;
        }
        FileStream Lock() {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath));
            return new FileStream(statePath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        public bool Enabled { get { var s=Read(); return s!=null && get(Cli)==s.Helper && get(Bridge)==s.Manifest && File.Exists(s.Helper); } }
        // Registration remains independent from the cached runtime's health. No discovery here.
        public string RuntimeHealth { get {
            try {
                var state = Read();
                if (state == null) return "unverified";
                if (!File.Exists(state.Manifest)) return "missing";
                var manifest = JsonData.Serializer().Deserialize<NativeBridgeSettings>(TextFile.Read(state.Manifest));
                if (manifest == null || String.IsNullOrWhiteSpace(manifest.RealCodex)) return "unverified";
                if (!File.Exists(manifest.RealCodex)) return "missing";
                return CodexRuntime.IsUsable(manifest.RealCodex) ? "healthy" : "stale";
            } catch { return "unverified"; }
        } }
        public string RegisteredHelper { get { var s=Read(); return s==null ? null : s.Helper; } }
        public static string ResolveHelper(string fallback) { return Current.RegisteredHelper ?? fallback; }
        public static string InstallHelper(string source)
        {
            var bytes=File.ReadAllBytes(source);
            var config=File.ReadAllBytes(source+".config");
            string hash=ContentHash(bytes, config);
            var directory=Path.Combine(LocalEnvironment.Current.DataDirectory,"native-host",hash);
            // Serialize publication of the complete bundle across threads and processes.
            // A reader must not race another publisher's rename on Windows.
            var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant());
            using (var gate = new System.Threading.Mutex(false, "Local\\OpenCodexLauncher.Helper." + ContentHash(identity, new byte[0]))) {
                bool acquired = false;
                try {
                    try { acquired = gate.WaitOne(TimeSpan.FromSeconds(30)); }
                    catch (System.Threading.AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Native helper deployment is busy. Retry the update.");
                    Directory.CreateDirectory(directory);
                    var target=Path.Combine(directory,"OpenCodexLauncher.exe");
                    CopyVerified(target,bytes); CopyVerified(target+".config",config);
                    return target;
                } finally { if (acquired) gate.ReleaseMutex(); }
            }
        }
        public static string ExpectedHelperPath(string source)
        {
            var bytes=File.ReadAllBytes(source); var config=File.ReadAllBytes(source+".config");
            return Path.Combine(LocalEnvironment.Current.DataDirectory,"native-host",ContentHash(bytes, config),"OpenCodexLauncher.exe");
        }
        static string ContentHash(byte[] bytes, byte[] config)
        {
            using(var sha=SHA256.Create()) { var combined=new byte[bytes.Length+config.Length]; Buffer.BlockCopy(bytes,0,combined,0,bytes.Length); Buffer.BlockCopy(config,0,combined,bytes.Length,config.Length); return BitConverter.ToString(sha.ComputeHash(combined)).Replace("-", ""); }
        }
        static void CopyVerified(string target, byte[] bytes)
        {
            if(File.Exists(target)) {
                if(Convert.ToBase64String(File.ReadAllBytes(target))!=Convert.ToBase64String(bytes)) throw new IOException("Native helper changed externally.");
            } else {
                var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
                try { File.WriteAllBytes(temp, bytes); File.Move(temp, target); }
                catch (IOException) {
                    if (File.Exists(target) && Convert.ToBase64String(File.ReadAllBytes(target))==Convert.ToBase64String(bytes)) return;
                    throw;
                }
                finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
            }
        }
        public string CheckHelper(string source)
        {
            try {
                var s = Read();
                if (s == null) return get(Cli) == null && get(Bridge) == null ? "disabled" : "conflict";
                try { Validate(s); } catch (InvalidOperationException) { return "conflict"; }
                if (get(Cli) != s.Helper || get(Bridge) != s.Manifest) return "recovery";
                if (!File.Exists(s.Manifest)) return "unverified";
                if (!File.Exists(s.Helper) || !File.Exists(s.Helper + ".config")) return "missing";
                // Verify actual bytes, not just the content-addressed directory name.
                return ContentHash(File.ReadAllBytes(source), File.ReadAllBytes(source + ".config")) ==
                    ContentHash(File.ReadAllBytes(s.Helper), File.ReadAllBytes(s.Helper + ".config")) ? "current" : "outdated";
            } catch { return "unverified"; }
        }
        public string HelperStatus
        {
            get
            {
                return CheckHelper(System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
        }
        void Validate(NativeActivationState s)
        {
            if(s==null) { if(get(Cli)!=null || get(Bridge)!=null) throw new InvalidOperationException(L.M("native.activationConflict")); return; }
            if((get(Cli)!=s.Helper && get(Cli)!=s.PreviousCli && get(Cli)!=s.TransitionCli) || (get(Bridge)!=s.Manifest && get(Bridge)!=s.PreviousBridge && get(Bridge)!=s.TransitionBridge))
                throw new InvalidOperationException(L.M("native.activationConflict"));
        }
        public async Task Enable(string helper, string manifest, Func<Task> prepare)
        {
            using(Lock()) {
            var previous=Read(); Validate(previous);
            await prepare();
            Validate(previous);
            Register(previous, helper, manifest);
            }
        }
        void Register(NativeActivationState previous, string helper, string manifest)
        {
            var next=new NativeActivationState {Helper=helper,Manifest=manifest,PreviousCli=previous==null?get(Cli):previous.PreviousCli,PreviousBridge=previous==null?get(Bridge):previous.PreviousBridge,TransitionCli=get(Cli),TransitionBridge=get(Bridge)};
            var oldCli=get(Cli); var oldBridge=get(Bridge);
            TextFile.AtomicWrite(statePath,JsonData.Serializer().Serialize(next),new UTF8Encoding(false));
            try { set(Cli,helper); set(Bridge,manifest); }
            catch {
                // Keep recovery record if registry restoration itself fails.
                set(Cli,oldCli); set(Bridge,oldBridge);
                if(previous==null) File.Delete(statePath); else TextFile.AtomicWrite(statePath,JsonData.Serializer().Serialize(previous),new UTF8Encoding(false));
                broadcast(); throw;
            }
            broadcast();
        }
        public void Disable()
        {
            using(Lock()) {
            var s=Read(); if(s==null) return; Validate(s);
            set(Cli,s.PreviousCli); set(Bridge,s.PreviousBridge);
            File.Delete(statePath); broadcast();
            // Keep helper and provider configuration for existing processes/credential commands.
            }
        }
        // Updates only the content-addressed helper registration. It deliberately does not
        // rerun Prepare, so provider/config/catalog state and running Desktop sessions stay intact.
        public string UpdateHelper(string source)
        {
            using(Lock()) {
                var previous = Read();
                if (previous == null) throw new InvalidOperationException("Native activation is not enabled.");
                Validate(previous);
                if (!File.Exists(previous.Manifest)) throw new InvalidDataException("Native manifest is missing. Restore Native configuration before updating the helper.");
                var helper = InstallHelper(source);
                Validate(previous);
                if (helper == previous.Helper && get(Cli) == helper && get(Bridge) == previous.Manifest) return helper;
                Register(previous, helper, previous.Manifest);
                return helper;
            }
        }
    }
}
