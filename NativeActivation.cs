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
        public string RegisteredHelper { get { var s=Read(); return s==null ? null : s.Helper; } }
        public static string ResolveHelper(string fallback) { return Current.RegisteredHelper ?? fallback; }
        public static string InstallHelper(string source)
        {
            var bytes=File.ReadAllBytes(source);
            var config=File.ReadAllBytes(source+".config");
            string hash;
            using(var sha=SHA256.Create()) {
                var combined=new byte[bytes.Length+config.Length];
                Buffer.BlockCopy(bytes,0,combined,0,bytes.Length); Buffer.BlockCopy(config,0,combined,bytes.Length,config.Length);
                hash=BitConverter.ToString(sha.ComputeHash(combined)).Replace("-", "");
            }
            var directory=Path.Combine(LocalEnvironment.Current.DataDirectory,"native-host",hash);
            Directory.CreateDirectory(directory);
            var target=Path.Combine(directory,"OpenCodexLauncher.exe");
            CopyVerified(target,bytes); CopyVerified(target+".config",config);
            return target;
        }
        static void CopyVerified(string target, byte[] bytes)
        {
            if(File.Exists(target)) {
                if(Convert.ToBase64String(File.ReadAllBytes(target))!=Convert.ToBase64String(bytes)) throw new IOException("Native helper changed externally.");
            } else File.WriteAllBytes(target,bytes);
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
    }
}
