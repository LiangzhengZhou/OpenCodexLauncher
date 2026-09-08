using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

namespace OpenCodexLauncherV2
{
    public static class SessionEvidence
    {
        public static RouteObservation Find(Dictionary<string, object> root, RunningSession session)
        {
            if (String.IsNullOrWhiteSpace(session.RequestSessionId)) return null;
            return JsonData.Array(JsonData.Value(root, "logs") ?? JsonData.Value(root, "data")).Select(JsonData.Object).Where(x => x != null)
                .Select(Parse).Where(x => x != null && x.SessionId == session.RequestSessionId && x.TimestampUtc >= session.StartedAt.ToUniversalTime() && x.TimestampUtc <= DateTime.UtcNow.AddSeconds(5))
                .OrderByDescending(x => x.TimestampUtc).FirstOrDefault();
        }
        public static RouteObservation Parse(Dictionary<string, object> row)
        {
            var sessionId = JsonData.Text(row, "sessionId", "conversationId");
            if (String.IsNullOrWhiteSpace(sessionId)) return null;
            DateTime stamp; var raw = JsonData.Value(row, "timestamp");
            if (raw is string)
            {
                if (!DateTime.TryParse((string)raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out stamp)) return null;
                stamp = stamp.ToUniversalTime();
            }
            else
            {
                double number;
                if (raw == null || !Double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number) || number <= 0) return null;
                if (number > 100000000000) number /= 1000;
                try { stamp = DateTimeOffset.FromUnixTimeSeconds((long)number).UtcDateTime; } catch (ArgumentOutOfRangeException) { return null; }
            }
            int status;
            if (!Int32.TryParse(Convert.ToString(JsonData.Value(row, "status") ?? JsonData.Value(row, "statusCode")), out status) || status < 200 || status >= 300) return null;
            var resolved = JsonData.Text(row, "resolvedModel");
            var provider = JsonData.Text(row, "provider");
            if (String.IsNullOrWhiteSpace(resolved) || String.IsNullOrWhiteSpace(provider)) return null;
            return new RouteObservation { SessionId = sessionId, Provider = provider, ResolvedModel = resolved,
                RequestedModel = JsonData.Text(row, "requestedModel"), TimestampUtc = stamp, StatusCode = status };
        }
        public static string Route(RouteObservation observation, string requested)
        {
            if (observation.ResolvedModel.StartsWith(observation.Provider + "/", StringComparison.Ordinal)) return observation.ResolvedModel;
            if (!requested.Contains("/") && observation.Provider == "openai") return observation.ResolvedModel;
            return observation.Provider + "/" + observation.ResolvedModel;
        }
    }
    public sealed class FallbackBudget
    {
        int remaining = 1;
        public bool TryUse(bool strict) { if (!strict || remaining == 0) return false; remaining--; return true; }
    }
}
