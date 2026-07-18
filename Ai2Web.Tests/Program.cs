using System.Text.Json;
using Ai2Web;

int failures = 0;
void Check(bool cond, string label, object? detail = null)
{
    Console.WriteLine((cond ? "PASS" : "FAIL") + "  " + label);
    if (!cond)
    {
        failures++;
        if (detail != null) Console.WriteLine("      got: " + JsonSerializer.Serialize(detail));
    }
}

// --- builder + validate ---
var m = Manifest.ForSite("Example Store", "https://store.example.com", "ecommerce")
    .Capability("content")
    .Capability("commerce", new Dictionary<string, object?> { ["endpoint"] = "/ai2w/products", ["checkout"] = true })
    .Capability("search", new Dictionary<string, object?> { ["endpoint"] = "/ai2w/search" })
    .Transports(new Dictionary<string, object?>
    {
        ["mcp"] = new Dictionary<string, object?> { ["enabled"] = true, ["endpoint"] = "/ai2w/mcp" },
        ["rest"] = new Dictionary<string, object?> { ["enabled"] = true },
    })
    .Auth(new Dictionary<string, object?> { ["methods"] = new List<object?> { "none", "oauth2" }, ["oauth2"] = new Dictionary<string, object?> { ["pkce"] = true } })
    .Consent(new Dictionary<string, object?> { ["requires_user_approval_for"] = new List<object?> { "purchase" } })
    .Contact(new Dictionary<string, object?> { ["support"] = "help@store.example.com" })
    .Identity(new Dictionary<string, object?> { ["legal_name"] = "Example Store Ltd" })
    .Build();

var r = Validator.Validate(m);
Check(m["protocol"] as string == "ai2w", "builder sets protocol ai2w");
Check(r.Valid, "manifest is valid", r.Errors);
Check(r.Score >= 90, "AI Readiness score >= 90", r.Score);
Check(r.Tier is "Standard" or "Enterprise", "tier Standard/Enterprise", r.Tier);

// --- negotiate ---
var neg = Negotiator.Negotiate(m, new Dictionary<string, object?>
{
    ["transports"] = new List<object?> { "mcp", "rest" },
    ["capabilities"] = new List<object?> { "content", "commerce", "flying" },
    ["auth"] = new List<object?> { "oauth2" },
});
Check(neg.Negotiated.Transport == "mcp", "negotiate picks mcp", neg.Negotiated.Transport);
Check(neg.Negotiated.Capabilities.Count == 2, "negotiate intersects caps", neg.Negotiated.Capabilities);
Check(neg.Unsupported.SequenceEqual(new[] { "flying" }), "negotiate reports unsupported", neg.Unsupported);
Check(neg.Negotiated.Auth == "oauth2", "negotiate selects oauth2", neg.Negotiated.Auth);

// --- server ---
Check(Server.Handle(m, "GET", "/ai2w").Status == 200, "server serves manifest");
Check(Server.Handle(m, "POST", "/ai2w").Status == 405, "manifest GET-only (405)");
var wk = Server.Handle(m, "GET", "/.well-known/ai2w", null, "https://store.example.com");
Check((wk.Body as Dictionary<string, object?>)?["ai2w"] as string == "https://store.example.com/ai2w", "well-known pointer", wk.Body);

// --- safety ---
Check(Safety.IsSafePublicUrl("https://store.example.com"), "ssrf allows public https");
Check(!Safety.IsSafePublicUrl("http://169.254.169.254/latest"), "ssrf blocks metadata");
Check(!Safety.IsSafePublicUrl("http://localhost:8080"), "ssrf blocks localhost");
Check(!Safety.IsSafePublicUrl("https://10.0.0.5/x"), "ssrf blocks private");

// SSRF bypass matrix: alternative IP encodings + IPv4-mapped IPv6 must all be blocked.
foreach (var u in new[] {
    "http://127.0.0.1/", "http://127.1/", "http://0/", "http://0.0.0.0/",
    "http://[::1]/", "http://[fd00::1]/", "http://[fe80::1]/",
    "http://172.16.0.1/", "http://192.168.1.1/", "http://100.64.0.1/",
    "http://2130706433/", "http://0x7f000001/", "http://0x7f.0.0.1/", "http://017700000001/",
    "http://[::ffff:127.0.0.1]/", "http://[::ffff:169.254.169.254]/",
    "ftp://127.0.0.1/", "file:///etc/passwd",
})
    Check(!Safety.IsSafePublicUrl(u), "ssrf blocks " + u);
foreach (var u in new[] {
    "https://api.stripe.com/v1", "https://fcbarcelona.com/",
    "http://93.184.216.34/", "https://[2606:4700::6810:85e5]/", "https://sub.domain.co.uk/path?q=1",
})
    Check(Safety.IsSafePublicUrl(u), "ssrf allows " + u);

// --- conformance contract ---
var casesPath = Path.Combine(AppContext.BaseDirectory, "conformance_cases.json");
var cases = JsonDocument.Parse(File.ReadAllText(casesPath)).RootElement;
foreach (var c in cases.EnumerateArray())
{
    var name = c.GetProperty("name").GetString()!;
    var manifest = (Dictionary<string, object?>)Json.ToObject(c.GetProperty("manifest"))!;
    var e = c.GetProperty("expect");
    var res = Validator.Validate(manifest);
    var probs = new List<string>();
    if (e.TryGetProperty("valid", out var ve) && res.Valid != ve.GetBoolean()) probs.Add($"valid={res.Valid}");
    if (e.TryGetProperty("tier", out var te) && res.Tier != te.GetString()) probs.Add($"tier={res.Tier}");
    if (e.TryGetProperty("minScore", out var mse) && res.Score < mse.GetInt32()) probs.Add($"score={res.Score}");
    if (e.TryGetProperty("errorsContain", out var ece) && !res.Errors.Any(x => x.Contains(ece.GetString()!))) probs.Add($"errors missing {ece.GetString()}");
    if (e.TryGetProperty("warns", out var we))
        foreach (var w in we.EnumerateArray())
        {
            var label = w.GetString();
            var chk = res.Checks.FirstOrDefault(x => x.Label == label);
            if (chk == null || chk.Ok) probs.Add($"expected warn {label}");
        }
    Check(probs.Count == 0, "conformance: " + name, probs.Count > 0 ? probs : null);
}

// --- request validation (Schema + server) ---
var schema = new Dictionary<string, object?>
{
    ["type"] = "object",
    ["properties"] = new Dictionary<string, object?>
    {
        ["order_id"] = new Dictionary<string, object?> { ["type"] = "string" },
        ["qty"] = new Dictionary<string, object?> { ["type"] = "integer" },
    },
    ["required"] = new List<object?> { "order_id" },
};
Check(Schema.Validate(new Dictionary<string, object?> { ["order_id"] = "A1", ["qty"] = 2.0 }, schema).Valid, "schema: valid input passes");
Check(!Schema.Validate(new Dictionary<string, object?> { ["qty"] = 2.0 }, schema).Valid, "schema: missing required fails");
Check(!Schema.Validate(new Dictionary<string, object?> { ["order_id"] = 5.0 }, schema).Valid, "schema: wrong type fails");
Check(!Schema.Validate(new Dictionary<string, object?> { ["order_id"] = "A1", ["qty"] = 1.5 }, schema).Valid, "schema: non-integer fails");
Check(Schema.Validate(new Dictionary<string, object?> { ["anything"] = 1 }, new Dictionary<string, object?>()).Valid, "schema: empty schema accepts anything");

var actMan = new Dictionary<string, object?>
{
    ["protocol"] = "ai2w",
    ["actions"] = new List<object?>
    {
        new Dictionary<string, object?>
        {
            ["name"] = "track_order",
            ["endpoint"] = "/ai2w/actions/track-order",
            ["input_schema"] = schema,
        },
    },
};
var acts = new Dictionary<string, Server.Handler> { ["track_order"] = _ => new Dictionary<string, object?> { ["ok"] = true } };
var okRes = Server.Handle(actMan, "POST", "/ai2w/actions/track-order", new Dictionary<string, object?> { ["order_id"] = "A1" }, actions: acts);
Check(okRes.Status == 200, "server: valid body -> 200", okRes.Status);
var badRes = Server.Handle(actMan, "POST", "/ai2w/actions/track-order", new Dictionary<string, object?>(), actions: acts);
var badErr = (badRes.Body as Dictionary<string, object?>)?["error"] as Dictionary<string, object?>;
Check(badRes.Status == 400 && badErr?["code"] as string == "invalid_request", "server: missing required -> 400 invalid_request", badRes.Body);
var offRes = Server.Handle(actMan, "POST", "/ai2w/actions/track-order", new Dictionary<string, object?>(), actions: acts, validateInput: false);
Check(offRes.Status == 200, "server: validateInput=false opt-out passes through", offRes.Status);

// --- v0.2 modules + export adapters (parity with @ai2web/core) ---
var m2 = Manifest.ForSite("Example Bistro", "https://bistro.example", "restaurant")
    .Capability("content")
    .Capability("commerce", new Dictionary<string, object?> { ["endpoint"] = "/ai2w/products" })
    .Capability("search", new Dictionary<string, object?> { ["endpoint"] = "/ai2w/search" })
    .Action(new Dictionary<string, object?>
    {
        ["name"] = "book_table", ["description"] = "Reserve a table.", ["method"] = "POST",
        ["endpoint"] = "/ai2w/actions/book-table", ["requires_auth"] = false, ["requires_user_approval"] = true,
        ["risk"] = "medium", ["intent"] = "reserve_table",
        ["bindings"] = new List<object?>
        {
            new Dictionary<string, object?> { ["kind"] = "mcp", ["ref"] = "book_table", ["priority"] = 1 },
            new Dictionary<string, object?> { ["kind"] = "redirect", ["ref"] = "/reserve", ["priority"] = 9, ["fallback_only"] = true },
        },
    })
    .Knowledge(new List<object?> { new Dictionary<string, object?> { ["id"] = "menu", ["name"] = "Menu", ["kind"] = "catalog", ["ref"] = "/ai2w/products", ["format"] = "json" } })
    .Governance(new Dictionary<string, object?> { ["rate_limits"] = new Dictionary<string, object?> { ["requests"] = 60, ["window_seconds"] = 60 }, ["consent_mode"] = new Dictionary<string, object?> { ["book_table"] = "explicit" } })
    .UsagePolicy(new Dictionary<string, object?> { ["bulk_extraction"] = false, ["model_training"] = false })
    .Legal(new Dictionary<string, object?> { ["jurisdiction"] = "EU", ["ai_transparency"] = true, ["ai_risk_classification"] = "limited" })
    .AgentIdentity(new Dictionary<string, object?> { ["required"] = false, ["allow_anonymous"] = true, ["methods"] = new List<object?> { "http_message_signatures" } })
    .Contact(new Dictionary<string, object?> { ["support"] = "hi@bistro.example" })
    .Build();

Check(m2["version"] is "0.2", "builder defaults to version 0.2", m2["version"]);
var gov = (m2["governance"] as Dictionary<string, object?>)?["rate_limits"] as Dictionary<string, object?>;
Check(gov?["requests"] is 60, "builder: governance");
Check((m2["usage_policy"] as Dictionary<string, object?>)?["model_training"] is false, "builder: usage_policy");
Check((m2["legal"] as Dictionary<string, object?>)?["ai_risk_classification"] is "limited", "builder: legal");
var agent = (m2["identity"] as Dictionary<string, object?>)?["agent"] as Dictionary<string, object?>;
Check((agent?["methods"] as List<object?>)?[0] is "http_message_signatures", "builder: agent identity");
Check(((m2["knowledge"] as List<object?>)?[0] as Dictionary<string, object?>)?["id"] is "menu", "builder: knowledge");
var act0 = (m2["actions"] as List<object?>)?[0] as Dictionary<string, object?>;
Check(act0?["intent"] is "reserve_table", "action: intent");
Check((act0?["bindings"] as List<object?>)?.Count == 2, "action: bindings");
Check(((act0?["bindings"] as List<object?>)?[1] as Dictionary<string, object?>)?["fallback_only"] is true, "action: fallback_only binding");

var txt = Export.ToLlmsTxt(m2);
Check(txt.StartsWith("# Example Bistro"), "llms.txt: title");
Check(txt.Contains("## Capabilities") && txt.Contains("- commerce"), "llms.txt: capabilities");
Check(txt.Contains("## Knowledge") && txt.Contains("Menu"), "llms.txt: knowledge");
Check(txt.Contains("book_table: Reserve a table."), "llms.txt: action");
Check(txt.Contains("https://bistro.example/ai2w"), "llms.txt: discovery link");

var aj = Export.ToAgentJson(m2);
Check(aj["name"] as string == "Example Bistro", "agent.json: name");
Check((aj["capabilities"] as List<string>)?.Contains("commerce") == true, "agent.json: capabilities");
var ajAct0 = (aj["actions"] as List<object?>)?[0] as Dictionary<string, object?>;
Check(ajAct0?["intent"] is "reserve_table", "agent.json: action intent");
Check((ajAct0?["bindings"] as List<object?>)?.Count == 2, "agent.json: bindings preserved");
var pol = aj["policies"] as Dictionary<string, object?>;
Check((pol?["legal"] as Dictionary<string, object?>)?["jurisdiction"] is "EU", "agent.json: legal in policies");
Check(((pol?["governance"] as Dictionary<string, object?>)?["consent_mode"] as Dictionary<string, object?>)?["book_table"] is "explicit", "agent.json: governance carried");
var ajDefault = Export.ToAgentJson(Manifest.ForSite("X", "https://x.example", "site")
    .Action(new Dictionary<string, object?> { ["name"] = "a", ["description"] = "d", ["method"] = "POST", ["endpoint"] = "/ai2w/actions/a", ["requires_auth"] = false, ["requires_user_approval"] = false, ["risk"] = "low" })
    .Build());
var defBind = ((ajDefault["actions"] as List<object?>)?[0] as Dictionary<string, object?>)?["bindings"] as List<object?>;
Check((defBind?[0] as Dictionary<string, object?>)?["kind"] is "rest", "agent.json: default rest binding");

// --- multi-surface serving (llms.txt + agent.json) ---
var llmsRes = Server.Handle(m2, "GET", "/llms.txt");
Check(llmsRes.Status == 200 && (llmsRes.Headers.GetValueOrDefault("content-type") ?? "").StartsWith("text/plain"), "server: /llms.txt text/plain");
Check(llmsRes.Body is string ls && ls.StartsWith("# Example Bistro"), "server: /llms.txt body");
var ajRes = Server.Handle(m2, "GET", "/.well-known/agent.json");
Check(ajRes.Status == 200 && (ajRes.Body as Dictionary<string, object?>)?["name"] as string == "Example Bistro", "server: /.well-known/agent.json");
var ajAlias = Server.Handle(m2, "GET", "/agent.json");
var ajAliasPol = (ajAlias.Body as Dictionary<string, object?>)?["policies"] as Dictionary<string, object?>;
Check(ajAlias.Status == 200 && ((ajAliasPol?["governance"] as Dictionary<string, object?>)?["rate_limits"] as Dictionary<string, object?>)?["requests"] is 60, "server: /agent.json alias + governance");
var llmsPost = Server.Handle(m2, "POST", "/llms.txt");
Check(llmsPost.Status == 405, "server: /llms.txt POST -> 405");

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
