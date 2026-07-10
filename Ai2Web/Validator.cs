using System.Text.RegularExpressions;

namespace Ai2Web;

public sealed record Check(bool Ok, int Points, string Label, string? Hint);

public sealed record Result(bool Valid, List<string> Errors, List<Check> Checks, int Score, string Tier);

/// <summary>Validation + AI Readiness scoring. Port of @ai2web/core validateManifest (spec §9/§11).</summary>
public static class Validator
{
    private static readonly Regex VersionRe = new(@"^\d+\.\d+(\.\d+)?$");

    public static Result Validate(Dictionary<string, object?> m)
    {
        var errors = new List<string>();
        var checks = new List<Check>();
        var caps = H.Map(m.GetValueOrDefault("capabilities")) ?? new();
        object? Cap(string n) => caps.GetValueOrDefault(n);

        if (!Equals(m.GetValueOrDefault("protocol"), "ai2w")) errors.Add("protocol must be 'ai2w'");
        if (!VersionRe.IsMatch(H.Str(m.GetValueOrDefault("version")))) errors.Add("version missing/invalid");
        var site = H.Map(m.GetValueOrDefault("site")) ?? new();
        foreach (var k in new[] { "name", "url", "type" })
            if (string.IsNullOrEmpty(H.Str(site.GetValueOrDefault(k)))) errors.Add($"site.{k} missing");
        if (caps.Count == 0) errors.Add("capabilities empty");

        var actionsExist = H.Has(Cap("actions"))
            || (H.List(m.GetValueOrDefault("actions"))?.Count ?? 0) > 0
            || H.Has(Cap("commerce")) || H.Has(Cap("booking"));

        var score = 0;
        void Add(bool ok, int points, string label, string hint)
        {
            checks.Add(new Check(ok, points, label, ok ? null : hint));
            if (ok) score += points;
        }

        Add(errors.Count == 0, 30, "Valid discovery manifest", "fix errors");
        Add(H.Has(Cap("content")), 6, "Content", "expose content module");
        Add(H.Has(Cap("commerce")) || H.Has(Cap("booking")) || H.Has(Cap("services")), 6, "Products / services / booking", "expose a commerce/services/booking module");
        Add(H.Has(Cap("search")), 4, "Search", "add a search capability");
        Add(actionsExist, 5, "Actions", "declare actions");
        Add(H.Has(Cap("events")), 6, "Events / subscriptions", "publish subscribable events");
        Add(H.BoolIn(m.GetValueOrDefault("agent_service"), "enabled"), 4, "Agent service (A2A)", "expose /ai2w/agent");

        var commerce = Cap("commerce");
        Add(!H.Has(commerce) || H.BoolIn(commerce, "checkout"), 4, "Checkout", "commerce present but checkout missing");

        var tr = H.Map(m.GetValueOrDefault("transports")) ?? new();
        Add(H.BoolIn(tr.GetValueOrDefault("mcp"), "enabled"), 8, "MCP transport", "expose an MCP endpoint");
        Add(H.BoolIn(tr.GetValueOrDefault("rest"), "enabled") || tr.GetValueOrDefault("feeds") != null, 4, "REST / feeds", "expose REST or feeds");

        var auth = H.Map(m.GetValueOrDefault("auth")) ?? new();
        var oauthOk = H.ContainsStr(H.List(auth.GetValueOrDefault("methods")), "oauth2") && H.BoolIn(auth.GetValueOrDefault("oauth2"), "pkce");
        var consentDeclared = (H.List(H.Map(m.GetValueOrDefault("consent"))?.GetValueOrDefault("requires_user_approval_for"))?.Count ?? 0) > 0;
        Add(!actionsExist || oauthOk, 8, "OAuth2 + PKCE", "protected actions need oauth2+pkce");
        Add(!actionsExist || consentDeclared, 7, "Consent declared", "declare consent for sensitive actions");

        Add(m.GetValueOrDefault("identity") != null, 4, "Identity", "add identity (legal_name, policies)");
        Add(m.GetValueOrDefault("contact") != null, 4, "Contact", "add support/security contact");

        if (score > 100) score = 100;

        var basic = errors.Count == 0;
        var standard = basic && m.GetValueOrDefault("transports") != null && (!actionsExist || consentDeclared) && m.GetValueOrDefault("contact") != null;
        var enterprise = standard && m.GetValueOrDefault("identity") != null && m.GetValueOrDefault("auth") != null && m.GetValueOrDefault("rate_limits") != null;
        var tier = enterprise ? "Enterprise" : standard ? "Standard" : basic ? "Basic" : "Invalid";

        return new Result(errors.Count == 0, errors, checks, score, tier);
    }
}
