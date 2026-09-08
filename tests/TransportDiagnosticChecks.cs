using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OpenCodexLauncherV2;

static class TransportDiagnosticChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var requests = new List<Uri>(); int tcpCount = 0;
        var input = new TransportProbeInputs {
            Tcp = (u,t) => { tcpCount++; return Task.FromResult(new TransportProbeResult { State=TransportProbeState.Connected }); },
            Get = (u,t) => { requests.Add(u); return Task.FromResult(new TransportProbeResult { State=TransportProbeState.HttpResponse, Status=u.AbsolutePath=="/healthz"?200:404 }); },
            Variable = n => "dummy-private-value", SystemProxyApplies = u => true
        };
        var data = await TransportDiagnostics.CollectAsync("http://127.0.0.1:14567/v1", input, CancellationToken.None);
        var json = JsonData.Serializer().Serialize(data);
        check(tcpCount==1&&requests.Select(u=>u.AbsolutePath).SequenceEqual(new[]{"/healthz","/v1/responses"}), "transport checks make one TCP attempt and only fixed health and responses GET requests");
        check(requests.All(u=>u.Port==14567), "transport diagnostics probe the target route port instead of assuming the default");
        check(json.Contains("404")&&json.Contains("not-tested")&&json.Contains("unverified"), "responses GET 404 remains an observation and does not claim POST failure or inference success");
        check(json.Contains("HTTP_PROXY_SET")&&!json.Contains("dummy-private")&&!json.Contains("http://"), "network report contains flags and codes but no proxy values or URLs");
        var invalid = new[]{"https://127.0.0.1/v1","http://example.invalid/v1","http://192.0.2.1/v1","http://127.0.0.1/private/path","http://dummy-secret@127.0.0.1/v1","http://127.0.0.1/v1?token=dummy-secret","http://127.0.0.1/v1#dummy-secret","not-a-uri",null};
        foreach(var route in invalid)
        {
            int before=requests.Count; int beforeTcp=tcpCount;
            data=await TransportDiagnostics.CollectAsync(route,input,CancellationToken.None);
            check(requests.Count==before&&tcpCount==beforeTcp&&data["status"].Equals("target-route-unsupported"), "unsafe or unsupported transport target performs zero probes");
        }
        check(TransportDiagnostics.ResponsesProbeUri("http://localhost:14567/v1/").Host=="127.0.0.1","localhost probe uses a literal IPv4 address without DNS");
        check(IPAddress.Parse(TransportDiagnostics.ResponsesProbeUri("http://[::1]:14567/v1").Host.Trim('[', ']')).Equals(IPAddress.IPv6Loopback),"IPv6 target retains its address family");
        check(TransportDiagnostics.ResponsesProbeUri("http://127.0.0.1:14567").AbsolutePath=="/responses","a root base URL is reported and probed without silently inserting v1");
        input.Tcp=(u,t)=>Task.FromResult(new TransportProbeResult{State=TransportProbeState.Refused});
        input.Get=(u,t)=> { throw new Exception("dummy-private-error"); };
        input.Variable=n=> { throw new Exception("dummy-private-env"); };
        input.SystemProxyApplies=u=> { throw new Exception("dummy-private-proxy"); };
        data=await TransportDiagnostics.CollectAsync("http://127.0.0.1:14567/v1",input,CancellationToken.None);
        json=JsonData.Serializer().Serialize(data);
        check(json.Contains("connection-refused")&&json.Contains("unavailable")&&!json.Contains("dummy"),"failed probes and proxy inspection expose no raw exceptions");
        input.Get=(u,t)=>Task.FromResult(new TransportProbeResult{State=(TransportProbeState)999,Status=123456789});
        data=await TransportDiagnostics.CollectAsync("http://127.0.0.1:14567/v1",input,CancellationToken.None);
        check(!JsonData.Serializer().Serialize(data).Contains("123456789"),"injected status and state values are allowlisted before serialization");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool cancelled=false;try { await TransportDiagnostics.CollectAsync("http://127.0.0.1/v1",input,cancel.Token); } catch(OperationCanceledException){cancelled=true;}
            check(cancelled,"transport diagnostics honor cancellation before all network access");
        }
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;
        try
        {
            var result=await TransportDiagnostics.ProbeTcp(new Uri("http://127.0.0.1:"+port+"/v1/responses"),CancellationToken.None);
            check(result.State==TransportProbeState.Connected,"real TCP probe reaches an isolated loopback listener");
        } finally { socket.Stop(); }
        var refused=await TransportDiagnostics.ProbeTcp(new Uri("http://127.0.0.1:"+port+"/v1/responses"),CancellationToken.None);
        check(refused.State==TransportProbeState.Refused || refused.State==TransportProbeState.TimedOut,"a closed port returns refusal or the bounded timeout, never connected");
        using(var listener=new HttpListener())
        {
            listener.Prefixes.Add("http://127.0.0.1:"+port+"/");listener.Start();
            foreach(var status in new[]{200,401,403,404,405,302})
            {
                var incoming=listener.GetContextAsync();
                var pending=TransportDiagnostics.ProbeGet(new Uri("http://127.0.0.1:"+port+"/v1/responses"),CancellationToken.None);
                var ctx=await incoming;
                bool safe=ctx.Request.HttpMethod=="GET"&&!ctx.Request.HasEntityBody&&ctx.Request.Headers["Authorization"]==null&&ctx.Request.Headers["Cookie"]==null;
                ctx.Response.StatusCode=status; if(status==302)ctx.Response.RedirectLocation="https://example.invalid/never-follow";
                ctx.Response.Close(); var result=await pending;
                check(safe&&result.Status==status,"real GET preserves HTTP status without inference, auth, cookies or following redirects");
            }
            var incomingTimeout=listener.GetContextAsync();
            var timeout=TransportDiagnostics.ProbeGet(new Uri("http://127.0.0.1:"+port+"/healthz"),CancellationToken.None);
            var slow=await incomingTimeout;var timed=await timeout;slow.Response.Close();
            check(timed.State==TransportProbeState.TimedOut,"HTTP probe has a bounded deadline even when a loopback listener stalls");
            using(var cancel=new CancellationTokenSource())
            {
                var incomingCancel=listener.GetContextAsync();
                var pending=TransportDiagnostics.ProbeGet(new Uri("http://127.0.0.1:"+port+"/healthz"),cancel.Token);
                var ctx=await incomingCancel;cancel.Cancel();bool cancelled=false;try{await pending;}catch(OperationCanceledException){cancelled=true;}ctx.Response.Close();
                check(cancelled,"closing the diagnostic window cancels an in-flight network probe");
            }
        }
        var blocked=await TransportDiagnostics.ProbeGet(new Uri("http://example.invalid/v1/responses"),CancellationToken.None);
        check(blocked.State==TransportProbeState.Unavailable,"production GET helper independently rejects non-loopback destinations");
    }
}
