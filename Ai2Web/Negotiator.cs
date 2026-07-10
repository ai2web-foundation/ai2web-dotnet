namespace Ai2Web;

public sealed record Negotiated(string? Transport, List<string> Capabilities, string? Auth, Dictionary<string, string> Endpoints);

public sealed record Negotiation(Negotiated Negotiated, List<string> Unsupported);

/// <summary>Capability negotiation (spec §5). Capability/unsupported lists are sorted
/// (a set - order is not normative) for deterministic output.</summary>
public static class Negotiator
{
    private static string EndpointOf(string name, object? v)
    {
        if (H.Map(v) is { } m && m.GetValueOrDefault("endpoint") is string s) return s;
        return $"/ai2w/{name}";
    }

    public static Negotiation Negotiate(Dictionary<string, object?> m, Dictionary<string, object?>? agent = null)
    {
        agent ??= new();
        var caps = H.Map(m.GetValueOrDefault("capabilities")) ?? new();
        var siteSet = new HashSet<string>();
        var siteList = new List<string>();
        foreach (var kv in caps)
            if (H.Has(kv.Value)) { siteSet.Add(kv.Key); siteList.Add(kv.Key); }

        var want = agent.GetValueOrDefault("capabilities") is { } wc && wc != null
            ? (H.List(wc) ?? new()).Select(H.Str).ToList()
            : siteList;
        var capsOut = want.Where(siteSet.Contains).ToList();
        var unsupported = want.Where(c => !siteSet.Contains(c)).ToList();
        capsOut.Sort(StringComparer.Ordinal);
        unsupported.Sort(StringComparer.Ordinal);

        var tr = H.Map(m.GetValueOrDefault("transports")) ?? new();
        var transportSet = new HashSet<string>();
        foreach (var kv in tr)
            if (H.BoolIn(kv.Value, "enabled")) transportSet.Add(kv.Key);
        var wantT = agent.GetValueOrDefault("transports") is { } wt && wt != null
            ? (H.List(wt) ?? new()).Select(H.Str).ToList()
            : transportSet.OrderBy(x => x, StringComparer.Ordinal).ToList();
        string? transport = wantT.FirstOrDefault(transportSet.Contains);

        var siteAuth = H.List(H.Map(m.GetValueOrDefault("auth"))?.GetValueOrDefault("methods"))?.Select(H.Str).ToList()
                       ?? new List<string> { "none" };
        if (siteAuth.Count == 0) siteAuth = new List<string> { "none" };
        var wantA = agent.GetValueOrDefault("auth") is { } wa && wa != null
            ? (H.List(wa) ?? new()).Select(H.Str).ToList()
            : siteAuth;

        string? auth;
        if (siteAuth.Contains("oauth2") && wantA.Contains("oauth2")) auth = "oauth2";
        else
        {
            auth = wantA.FirstOrDefault(siteAuth.Contains);
            if (auth == null && siteAuth.Contains("none")) auth = "none";
        }

        var endpoints = new Dictionary<string, string>();
        foreach (var c in capsOut) endpoints[c] = EndpointOf(c, caps.GetValueOrDefault(c));
        if (transport != null && H.Map(tr.GetValueOrDefault(transport))?.GetValueOrDefault("endpoint") is string ep)
            endpoints[transport] = ep;

        return new Negotiation(new Negotiated(transport, capsOut, auth, endpoints), unsupported);
    }
}
