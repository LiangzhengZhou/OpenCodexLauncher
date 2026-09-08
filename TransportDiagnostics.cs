using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCodexLauncherV2
{
    public enum TransportProbeState { Unavailable, Connected, Refused, TimedOut, HttpResponse }
    public sealed class TransportProbeResult
    {
        public TransportProbeState State;
        public int? Status;
    }
    public sealed class TransportProbeInputs
    {
        public Func<Uri, CancellationToken, Task<TransportProbeResult>> Tcp, Get;
        public Func<string, string> Variable;
        public Func<Uri, bool?> SystemProxyApplies;
        public static TransportProbeInputs Local()
        {
            var env = LocalEnvironment.Current;
            return new TransportProbeInputs { Tcp = TransportDiagnostics.ProbeTcp, Get = TransportDiagnostics.ProbeGet, Variable = env.Variable,
                SystemProxyApplies = uri => { var proxy = WebRequest.DefaultWebProxy; return proxy != null && !proxy.IsBypassed(uri); } };
        }
    }
    public static class TransportDiagnostics
    {
        // Only fixed read-only paths on literal loopback addresses are requested.
        // Never POST, read credentials, send auth/cookies, follow redirects or use a proxy.
        public static Uri ResponsesProbeUri(string baseUrl)
        {
            Uri uri; IPAddress address;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out uri) || uri.Scheme != "http" ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
            var host = uri.Host.Trim('[', ']');
            if (String.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) address = IPAddress.Loopback;
            else if (!IPAddress.TryParse(host, out address) || !IPAddress.IsLoopback(address)) return null;
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path != "" && path != "/v1") return null;
            return new UriBuilder("http", address.ToString(), uri.Port, path + "/responses").Uri;
        }
        static string State(TransportProbeState state)
        {
            switch (state) {
                case TransportProbeState.Connected: return "connected";
                case TransportProbeState.Refused: return "connection-refused";
                case TransportProbeState.TimedOut: return "timed-out";
                case TransportProbeState.HttpResponse: return "http-response";
                default: return "unavailable";
            }
        }
        static Dictionary<string, object> Safe(TransportProbeResult value)
        {
            value = value ?? new TransportProbeResult();
            return new Dictionary<string, object> { { "state", State(value.State) },
                { "httpStatus", value.Status >= 100 && value.Status <= 599 ? value.Status : null } };
        }
        static async Task<TransportProbeResult> Attempt(Func<Uri, CancellationToken, Task<TransportProbeResult>> probe, Uri uri, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try { return probe == null ? new TransportProbeResult() : await probe(uri, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return new TransportProbeResult { State = TransportProbeState.TimedOut }; }
            catch { return new TransportProbeResult(); }
        }
        public static async Task<Dictionary<string, object>> CollectAsync(string baseUrl, TransportProbeInputs input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var data = new Dictionary<string, object> { { "scope", "launcher-process-direct-loopback-get-only" },
                { "postAuthentication", "not-tested" }, { "inference", "not-tested" },
                { "desktopProcessConnectivity", "not-tested" }, { "serviceIdentity", "unverified" } };
            var uri = ResponsesProbeUri(baseUrl);
            if (uri == null) { data["status"] = "target-route-unsupported"; return data; }
            data["status"] = "completed";
            data["loopbackPort"] = uri.Port;
            data["responsesPath"] = uri.AbsolutePath;
            data["localhostProbeUsesIpv4"] = new Uri(baseUrl).Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
            var health = new UriBuilder(uri) { Path = "/healthz" }.Uri;
            // One TCP attempt plus two GETs: each production probe has a 1s deadline.
            data["tcp"] = Safe(await Attempt(input.Tcp, uri, token).ConfigureAwait(false));
            data["healthGet"] = Safe(await Attempt(input.Get, health, token).ConfigureAwait(false));
            data["responsesGet"] = Safe(await Attempt(input.Get, uri, token).ConfigureAwait(false));
            var flags = new Dictionary<string, object>();
            foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" })
            {
                token.ThrowIfCancellationRequested();
                try { flags[name + "_SET"] = input.Variable == null ? (object)null : !String.IsNullOrWhiteSpace(input.Variable(name)); }
                catch { flags[name + "_SET"] = null; }
            }
            data["launcherEnvironment"] = flags;
            token.ThrowIfCancellationRequested();
            try { data["launcherSystemProxyAppliesToLoopback"] = input.SystemProxyApplies == null ? null : input.SystemProxyApplies(uri); }
            catch { data["launcherSystemProxyAppliesToLoopback"] = null; }
            return data;
        }
        public static async Task<TransportProbeResult> ProbeTcp(Uri uri, CancellationToken token)
        {
            IPAddress address;
            if (uri == null || uri.Scheme != "http" || uri.UserInfo.Length != 0 ||
                !IPAddress.TryParse(uri.Host.Trim('[', ']'), out address) || !IPAddress.IsLoopback(address)) return new TransportProbeResult();
            token.ThrowIfCancellationRequested();
            using (var client = new TcpClient(address.AddressFamily))
            {
                try
                {
                    var connect = client.ConnectAsync(address, uri.Port);
                    if (await Task.WhenAny(connect, Task.Delay(1000, token)).ConfigureAwait(false) != connect)
                    {
                        client.Close();
                        ObserveFailure(connect);
                        token.ThrowIfCancellationRequested();
                        return new TransportProbeResult { State = TransportProbeState.TimedOut };
                    }
                    await connect.ConfigureAwait(false);
                    return new TransportProbeResult { State = TransportProbeState.Connected };
                }
                catch (SocketException e) { return new TransportProbeResult { State = e.SocketErrorCode == SocketError.ConnectionRefused ? TransportProbeState.Refused : TransportProbeState.Unavailable }; }
            }
        }
        static void ObserveFailure(Task task)
        { task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        public static async Task<TransportProbeResult> ProbeGet(Uri uri, CancellationToken token)
        {
            IPAddress address;
            if (uri == null || uri.Scheme != "http" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                !IPAddress.TryParse(uri.Host.Trim('[', ']'), out address) || !IPAddress.IsLoopback(address) ||
                (uri.AbsolutePath != "/healthz" && uri.AbsolutePath != "/v1/responses" && uri.AbsolutePath != "/responses")) return new TransportProbeResult();
            token.ThrowIfCancellationRequested();
            using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(1) })
            {
                try
                {
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                        return new TransportProbeResult { State = TransportProbeState.HttpResponse, Status = (int)response.StatusCode };
                }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return new TransportProbeResult { State = TransportProbeState.TimedOut }; }
                catch (HttpRequestException) { return new TransportProbeResult(); }
            }
        }
    }
}
