using System.Security.Cryptography;

namespace Ai2Web;

/// <summary>
/// NLWeb (nlweb.ai) interop primitives.
///
/// NLWeb turns a site's content into a natural-language, schema.org-flavoured query endpoint (its
/// <c>ask</c> API). These helpers let an AI2Web site advertise an NLWeb surface in its manifest and
/// serve a minimal, NLWeb-compatible <c>ask</c> response over its own content, so agents that speak
/// NLWeb can query the site without it deploying the full NLWeb stack.
///
/// The search itself is application-specific (a pure toolkit): the app finds the matching content
/// items and passes them in; <see cref="AskResponse"/> shapes them into NLWeb's result envelope
/// (list mode, schema.org Item results; pass an answer for generate mode). NLWeb defines no
/// discovery file, so <see cref="Transport"/> is an AI2Web convention pointing at the site's
/// <c>/ask</c> (and <c>/mcp</c>) URLs.
/// </summary>
public static class Nlweb
{
    public const string Version = "0.55";
    private const string DefaultAsk = "/ai2w/nlweb/ask";
    private const string DefaultMcp = "/ai2w/nlweb/mcp";

    /// <summary>The transports.nlweb advertisement to merge into a manifest.</summary>
    public static Dictionary<string, object?> Transport(Dictionary<string, object?>? overrides = null)
    {
        var t = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["version"] = Version,
            ["ask"] = DefaultAsk,
            ["mcp"] = DefaultMcp,
            ["modes"] = new List<object?> { "list" },
        };
        if (overrides != null)
        {
            foreach (var kv in overrides) t[kv.Key] = kv.Value;
        }
        return t;
    }

    /// <summary>Wrap one content item into an NLWeb result Item.</summary>
    public static Dictionary<string, object?> Item(Dictionary<string, object?> content, string? site = null, string? siteUrl = null)
    {
        string name = Str(content, "name");
        if (name == "") name = Str(content, "title");
        string s = Str(content, "site");
        if (s == "") s = site ?? "";
        string su = Str(content, "siteUrl");
        if (su == "") su = siteUrl ?? "";
        int score = 100;
        if (content.TryGetValue("score", out var sc) && sc != null)
        {
            score = sc switch { int i => i, long l => (int)l, double d => (int)d, _ => 100 };
        }
        var schema = content.GetValueOrDefault("schema_object") as Dictionary<string, object?> ?? SchemaObject(content);
        return new Dictionary<string, object?>
        {
            ["@type"] = "Item",
            ["url"] = Str(content, "url"),
            ["name"] = name,
            ["site"] = s,
            ["siteUrl"] = su,
            ["score"] = score,
            ["description"] = Str(content, "description"),
            ["schema_object"] = schema,
        };
    }

    /// <summary>Build a minimal buffered NLWeb ask response (list mode) from matched content items.</summary>
    public static Dictionary<string, object?> AskResponse(
        string query,
        IEnumerable<Dictionary<string, object?>> items,
        string? site = null,
        string? siteUrl = null,
        string? queryId = null,
        string? answer = null)
    {
        var results = new List<object?>();
        foreach (var it in items) results.Add(Item(it, site, siteUrl));
        var resp = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["query_id"] = queryId ?? ("q_" + RandHex(8)),
            ["message_type"] = "result",
            ["results"] = results,
        };
        if (!string.IsNullOrEmpty(answer))
        {
            resp["answer"] = new Dictionary<string, object?> { ["@type"] = "GeneratedAnswer", ["answer"] = answer, ["items"] = results };
        }
        return resp;
    }

    private static Dictionary<string, object?> SchemaObject(Dictionary<string, object?> c)
    {
        string t = Str(c, "type");
        if (t == "") t = "Thing";
        var obj = new Dictionary<string, object?> { ["@type"] = t };
        string name = Str(c, "name");
        if (name == "") name = Str(c, "title");
        if (name != "") obj["name"] = name;
        if (Str(c, "url") != "") obj["url"] = Str(c, "url");
        if (Str(c, "description") != "") obj["description"] = Str(c, "description");
        return obj;
    }

    private static string Str(Dictionary<string, object?> m, string key) => m.GetValueOrDefault(key) as string ?? "";

    private static string RandHex(int n) => Convert.ToHexString(RandomNumberGenerator.GetBytes(n)).ToLowerInvariant();
}
