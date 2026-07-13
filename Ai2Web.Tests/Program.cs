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

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
