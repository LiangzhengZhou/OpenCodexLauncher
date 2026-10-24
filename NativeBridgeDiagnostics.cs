using System;
using System.IO;
using System.Text;
using System.Threading;

namespace OpenCodexLauncherV2
{
    // Bounded diagnostics for the JSON-RPC bridge. This API deliberately accepts
    // only allowlisted tokens, counters and booleans: it must never receive a
    // prompt, JSON payload, stderr text, credential, token or user path.
    public static class NativeBridgeDiagnostics
    {
        const long MaxBytes = 128 * 1024;
        static readonly object gate = new object();
        static int methods;

        public static void RuntimeSelected(string runtimeId, bool automatic)
        {
            Write("runtime", "id=" + Safe(runtimeId) + " automatic=" + (automatic ? "true" : "false"));
        }

        public static void ChildStarted(int pid)
        {
            Write("child", "started pid=" + pid.ToString());
        }

        public static void ChildExit(int pid, int exitCode, long stderrBytes)
        {
            Write("child", "exit pid=" + pid.ToString() + " code=" + exitCode.ToString() + " stderr_bytes=" + stderrBytes.ToString());
        }

        public static void Method(string method)
        {
            if (Interlocked.Increment(ref methods) > 64) return;
            Write("method", Safe(method));
        }

        public static void ResponseSchema(bool hasConfigRequirements, bool hasRequirements, bool hasAppServerVersion)
        {
            Write("configRequirements/read", "response=" + (hasConfigRequirements ? "present" : "absent")
                + " requirements=" + (hasRequirements ? "present" : "absent")
                + " appServerVersion=" + (hasAppServerVersion ? "present" : "absent"));
        }

        public static void Failure(string code, string errorType, int hresult)
        {
            Write("failure", "code=" + Safe(code) + " type=" + Safe(errorType) + " hresult=0x" + hresult.ToString("X8"));
        }

        static string Safe(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "none";
            var builder = new StringBuilder(Math.Min(value.Length, 96));
            foreach (var c in value)
            {
                if (builder.Length >= 96) break;
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                    c == '.' || c == '_' || c == '-' || c == '/' || c == ':') builder.Append(c);
            }
            return builder.Length == 0 ? "other" : builder.ToString();
        }

        static void Write(string category, string payload)
        {
            try
            {
                var directory = Path.Combine(LocalEnvironment.Current.DataDirectory, "logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "native-bridge.log");
                lock (gate)
                {
                    try
                    {
                        if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                        {
                            var previous = path + ".1";
                            if (File.Exists(previous)) File.Delete(previous);
                            File.Move(path, previous);
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + category + " " + payload + "\n", new UTF8Encoding(false));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
