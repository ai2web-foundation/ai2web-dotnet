using System.Text.RegularExpressions;

namespace Ai2Web;

public sealed record Response(int Status, Dictionary<string, string> Headers, object? Body);

/// <summary>Framework-agnostic AI2Web router. Port of @ai2web/server. Adapt to ASP.NET, etc.</summary>
public static class Server
{
    public delegate object? Handler(object? body);

    private static readonly Dictionary<string, string> Cors = new()
    {
        ["access-control-allow-origin"] = "*",
        ["access-control-allow-methods"] = "GET, POST, OPTIONS",
        ["access-control-allow-headers"] = "content-type, authorization",
    };

    private static readonly Regex ActionRe = new(@"^/ai2w/actions/([a-z0-9_-]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex ModuleRe = new(@"^/ai2w/([a-z0-9_-]+)$", RegexOptions.IgnoreCase);

    private static Response Json(int status, object? body)
    {
        var h = new Dictionary<string, string> { ["content-type"] = "application/json; charset=utf-8" };
        foreach (var kv in Cors) h[kv.Key] = kv.Value;
        return new Response(status, h, body);
    }

    private static Response Error(int status, string code, string message) =>
        Json(status, new Dictionary<string, object?> { ["error"] = new Dictionary<string, object?> { ["code"] = code, ["message"] = message, ["retryable"] = false } });

    private static Response Text(int status, string contentType, string body)
    {
        var h = new Dictionary<string, string> { ["content-type"] = contentType };
        foreach (var kv in Cors) h[kv.Key] = kv.Value;
        return new Response(status, h, body);
    }

    private static object? ActionInputSchema(Dictionary<string, object?> manifest, string name)
    {
        foreach (var a in H.List(manifest.GetValueOrDefault("actions")) ?? new())
            if (H.Map(a) is { } am && am.GetValueOrDefault("name") as string == name)
                return am.GetValueOrDefault("input_schema");
        return null;
    }

    public static Response Handle(
        Dictionary<string, object?> manifest,
        string method,
        string path,
        object? body = null,
        string? origin = null,
        Dictionary<string, Handler>? modules = null,
        Dictionary<string, Handler>? actions = null,
        bool validateInput = true)
    {
        modules ??= new();
        actions ??= new();

        var trimmed = path.Trim('/');
        path = trimmed.Length == 0 ? "/" : "/" + trimmed;
        method = method.ToUpperInvariant();

        if (method == "OPTIONS") return new Response(204, Cors, null);

        if (path == "/.well-known/ai2w")
            return origin != null
                ? Json(200, new Dictionary<string, object?> { ["ai2w"] = origin.TrimEnd('/') + "/ai2w" })
                : Json(200, manifest);

        if (path is "/ai2w" or "/ai" or "/.ai")
            return method != "GET" ? Error(405, "invalid_request", "Use GET for the manifest.") : Json(200, manifest);

        // Multi-surface projections (RFC-0015): the one canonical manifest, emitted in other
        // discovery formats so agents that speak llms.txt or agent.json need not parse ai2w first.
        if (path == "/llms.txt")
            return method != "GET"
                ? Error(405, "invalid_request", "Use GET for llms.txt.")
                : Text(200, "text/plain; charset=utf-8", Export.ToLlmsTxt(manifest));

        if (path is "/.well-known/agent.json" or "/agent.json")
            return method != "GET"
                ? Error(405, "invalid_request", "Use GET for agent.json.")
                : Json(200, Export.ToAgentJson(manifest));

        if (path == "/ai2w/negotiate")
        {
            var supports = new Dictionary<string, object?>();
            if (H.Map(body) is { } bm)
            {
                if (H.Map(bm.GetValueOrDefault("agent"))?.GetValueOrDefault("supports") is Dictionary<string, object?> s) supports = s;
                else if (H.Map(bm.GetValueOrDefault("supports")) is { } s2) supports = s2;
                else supports = bm;
            }
            return Json(200, Negotiator.Negotiate(manifest, supports));
        }

        var am = ActionRe.Match(path);
        if (am.Success)
        {
            var name = am.Groups[1].Value.Replace("-", "_");
            if (!actions.TryGetValue(name, out var fn))
                return Error(404, "unsupported_capability", $"Unknown action '{name}'.");
            if (validateInput && ActionInputSchema(manifest, name) is { } inputSchema)
            {
                var r = Schema.Validate(body ?? new Dictionary<string, object?>(), inputSchema);
                if (!r.Valid)
                    return Error(400, "invalid_request", "Request does not match the declared input schema: " + string.Join("; ", r.Errors) + ".");
            }
            return Json(200, fn(body));
        }

        var mm = ModuleRe.Match(path);
        if (mm.Success)
        {
            var name = mm.Groups[1].Value;
            return modules.TryGetValue(name, out var fn)
                ? Json(200, fn(body))
                : Error(404, "unsupported_capability", $"Module '{name}' not exposed.");
        }

        return Error(404, "invalid_request", $"No AI2Web route for {path}.");
    }
}
