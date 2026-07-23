namespace Ai2Web;

/// <summary>
/// Export adapters (RFC-0015): project the one canonical AI2Web manifest into other wire formats
/// and discovery surfaces. Mirrors @ai2web/core's export.ts.
///
/// Each export is a best-effort projection; where a target cannot represent a field, it is omitted
/// rather than misstated. The canonical /ai2w manifest stays authoritative for execution.
/// </summary>
public static class Export
{
    private static bool Enabled(object? v) =>
        v is true || (v is Dictionary<string, object?> d && d.GetValueOrDefault("enabled") is true);

    private static List<string> EnabledCapabilities(Dictionary<string, object?> m)
    {
        var caps = m.GetValueOrDefault("capabilities") as Dictionary<string, object?> ?? new();
        var outp = new List<string>();
        foreach (var kv in caps)
            if (Enabled(kv.Value)) outp.Add(kv.Key);
        return outp;
    }

    private static string Str(object? v) => v as string ?? "";

    /// <summary>Project the manifest to an llms.txt document: a plain-text summary and links a
    /// model can read for content and guidance. Reads only; no actions are exposed here.</summary>
    public static string ToLlmsTxt(Dictionary<string, object?> m)
    {
        var site = m.GetValueOrDefault("site") as Dictionary<string, object?> ?? new();
        var baseUrl = Str(site.GetValueOrDefault("url")).TrimEnd('/');
        var lines = new List<string> { "# " + Str(site.GetValueOrDefault("name")) };
        var desc = Str(site.GetValueOrDefault("description"));
        if (desc != "") { lines.Add(""); lines.Add("> " + desc); }

        var caps = EnabledCapabilities(m);
        if (caps.Count > 0)
        {
            lines.Add(""); lines.Add("## Capabilities");
            foreach (var c in caps) lines.Add("- " + c);
        }

        if (m.GetValueOrDefault("knowledge") is List<object?> kn && kn.Count > 0)
        {
            lines.Add(""); lines.Add("## Knowledge");
            foreach (var ki in kn)
            {
                var k = ki as Dictionary<string, object?> ?? new();
                var refv = Str(k.GetValueOrDefault("ref"));
                if (!refv.StartsWith("http"))
                    refv = baseUrl + (refv.StartsWith("/") ? "" : "/") + refv;
                var name = Str(k.GetValueOrDefault("name"));
                if (name == "") name = Str(k.GetValueOrDefault("id"));
                lines.Add("- [" + name + "](" + refv + ")");
            }
        }

        if (m.GetValueOrDefault("actions") is List<object?> acts && acts.Count > 0)
        {
            lines.Add(""); lines.Add("## Actions");
            foreach (var ai in acts)
            {
                var a = ai as Dictionary<string, object?> ?? new();
                lines.Add("- " + Str(a.GetValueOrDefault("name")) + ": " + Str(a.GetValueOrDefault("description")));
            }
        }

        lines.Add(""); lines.Add("## Discovery"); lines.Add("- Manifest: " + baseUrl + "/ai2w");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Project the manifest to a generic agent.json style capability document. Best-effort,
    /// format-neutral projection of identity, capabilities, actions (with bindings), knowledge and
    /// policies. Consent/governance a target cannot express are carried as a policies object.</summary>
    public static Dictionary<string, object?> ToAgentJson(Dictionary<string, object?> m)
    {
        var site = m.GetValueOrDefault("site") as Dictionary<string, object?> ?? new();
        var consent = m.GetValueOrDefault("consent") as Dictionary<string, object?> ?? new();

        var actions = new List<object?>();
        if (m.GetValueOrDefault("actions") is List<object?> acts)
        {
            foreach (var ai in acts)
            {
                var a = ai as Dictionary<string, object?> ?? new();
                object? bindings = a.GetValueOrDefault("bindings")
                    ?? new List<object?> { new Dictionary<string, object?> { ["kind"] = "rest", ["ref"] = a.GetValueOrDefault("endpoint") } };
                actions.Add(new Dictionary<string, object?>
                {
                    ["name"] = a.GetValueOrDefault("name"),
                    ["intent"] = a.GetValueOrDefault("intent"),
                    ["description"] = a.GetValueOrDefault("description"),
                    ["risk"] = a.GetValueOrDefault("risk"),
                    ["requires_consent"] = a.GetValueOrDefault("requires_user_approval"),
                    ["requires_auth"] = a.GetValueOrDefault("requires_auth"),
                    ["input_schema"] = a.GetValueOrDefault("input_schema"),
                    ["bindings"] = bindings,
                });
            }
        }

        return new Dictionary<string, object?>
        {
            ["schema"] = "agent-capabilities",
            ["name"] = site.GetValueOrDefault("name"),
            ["description"] = site.GetValueOrDefault("description"),
            ["url"] = site.GetValueOrDefault("url"),
            ["identity"] = m.GetValueOrDefault("identity"),
            ["capabilities"] = EnabledCapabilities(m),
            ["actions"] = actions,
            ["knowledge"] = m.GetValueOrDefault("knowledge"),
            ["transports"] = m.GetValueOrDefault("transports"),
            ["policies"] = new Dictionary<string, object?>
            {
                ["consent"] = consent.GetValueOrDefault("requires_user_approval_for"),
                ["governance"] = m.GetValueOrDefault("governance"),
                ["usage"] = m.GetValueOrDefault("usage_policy"),
                ["legal"] = m.GetValueOrDefault("legal"),
            },
        };
    }

    /// <summary>OAuth 2.0 Protected Resource metadata (RFC 9728), for
    /// /.well-known/oauth-protected-resource. MCP clients read this to discover which
    /// authorization server guards the resource. Null when the site does not advertise oauth2.</summary>
    public static Dictionary<string, object?>? ToOAuthProtectedResource(Dictionary<string, object?> m)
    {
        var auth = m.GetValueOrDefault("auth") as Dictionary<string, object?> ?? new();
        var methods = auth.GetValueOrDefault("methods") as IEnumerable<object?> ?? Array.Empty<object?>();
        if (!methods.Any(x => Str(x) == "oauth2")) return null;

        var site = m.GetValueOrDefault("site") as Dictionary<string, object?> ?? new();
        var baseUrl = Str(site.GetValueOrDefault("url")).TrimEnd('/');
        var o2 = auth.GetValueOrDefault("oauth2") as Dictionary<string, object?> ?? new();
        var issuer = baseUrl;
        var authz = Str(o2.GetValueOrDefault("authorization_url"));
        if (authz != "" && Uri.TryCreate(authz, UriKind.Absolute, out var u))
            issuer = u.Scheme + "://" + u.Authority;

        var doc = new Dictionary<string, object?>
        {
            ["resource"] = baseUrl + "/ai2w",
            ["authorization_servers"] = new List<string> { issuer },
            ["bearer_methods_supported"] = new List<string> { "header" },
        };
        if (o2.GetValueOrDefault("scopes") is IEnumerable<object?> sc && sc.Any())
            doc["scopes_supported"] = sc.Select(Str).ToList();
        return doc;
    }

    /// <summary>Map usage_policy onto Content Signals tokens. `search` stays yes because AI2Web
    /// exists to be discoverable; AI signals are only asserted when the manifest states them, so
    /// an unset policy is never reported as a refusal. Null when no policy is declared.</summary>
    public static string? ToContentSignals(Dictionary<string, object?> m)
    {
        if (m.GetValueOrDefault("usage_policy") is not Dictionary<string, object?> p || p.Count == 0) return null;
        var signals = new List<string> { "search=yes" };
        if (p.GetValueOrDefault("content_reproduction") is bool cr) signals.Add("ai-input=" + (cr ? "yes" : "no"));
        if (p.GetValueOrDefault("model_training") is bool mt) signals.Add("ai-train=" + (mt ? "yes" : "no"));
        return string.Join(", ", signals);
    }

    /// <summary>A robots.txt FRAGMENT carrying the usage policy and a manifest pointer. Append to
    /// an existing robots.txt; never a replacement, and it emits no Disallow rules.</summary>
    public static string ToRobotsTxt(Dictionary<string, object?> m)
    {
        var site = m.GetValueOrDefault("site") as Dictionary<string, object?> ?? new();
        var baseUrl = Str(site.GetValueOrDefault("url")).TrimEnd('/');
        var lines = new List<string> { "# AI2Web usage policy, projected from " + baseUrl + "/ai2w", "User-agent: *" };
        var signals = ToContentSignals(m);
        if (signals != null) lines.Add("Content-Signal: " + signals);
        if (m.GetValueOrDefault("usage_policy") is Dictionary<string, object?> up
            && up.GetValueOrDefault("bulk_extraction") is false)
            lines.Add("# bulk_extraction: false - please use the /ai2w endpoints instead of crawling");
        lines.Add("# AI2Web-Manifest: " + baseUrl + "/ai2w");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Value for an HTTP Link header advertising the manifest to non-HTML clients.</summary>
    public static string ToDiscoveryLinkHeader(Dictionary<string, object?> m)
    {
        var site = m.GetValueOrDefault("site") as Dictionary<string, object?> ?? new();
        return "<" + Str(site.GetValueOrDefault("url")).TrimEnd('/') + "/ai2w>; rel=\"ai2w\"";
    }
}
