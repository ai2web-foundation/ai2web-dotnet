using System.Text.Json;

namespace Ai2Web;

/// <summary>Fluent AI2Web (ai2w) manifest builder - "describe your website once".</summary>
public sealed class Manifest
{
    private readonly Dictionary<string, object?> _m;

    public Manifest(Dictionary<string, object?> site)
    {
        _m = new Dictionary<string, object?>
        {
            ["protocol"] = "ai2w",
            ["version"] = "0.1",
            ["site"] = site,
            ["capabilities"] = new Dictionary<string, object?>(),
        };
    }

    public static Manifest ForSite(string name, string url, string type) =>
        new(new Dictionary<string, object?> { ["name"] = name, ["url"] = url, ["type"] = type });

    public Manifest Capability(string name, object? value = null)
    {
        value ??= true;
        if (value is Dictionary<string, object?> obj)
        {
            var merged = new Dictionary<string, object?> { ["enabled"] = true };
            foreach (var kv in obj) merged[kv.Key] = kv.Value;
            value = merged;
        }
        ((Dictionary<string, object?>)_m["capabilities"]!)[name] = value;
        return this;
    }

    public Manifest Transports(Dictionary<string, object?> t) { Merge("transports", t); return this; }
    public Manifest Auth(Dictionary<string, object?> a) { _m["auth"] = a; return this; }
    public Manifest Consent(Dictionary<string, object?> c) { _m["consent"] = c; return this; }
    public Manifest Identity(Dictionary<string, object?> i) { _m["identity"] = i; return this; }
    public Manifest Contact(Dictionary<string, object?> c) { _m["contact"] = c; return this; }

    public Manifest Action(Dictionary<string, object?> a)
    {
        if (_m.GetValueOrDefault("actions") is not List<object?> list) { list = new(); _m["actions"] = list; }
        list.Add(a);
        return Capability("actions", new Dictionary<string, object?> { ["endpoint"] = "/ai2w/actions" });
    }

    public Manifest Events(Dictionary<string, object?> e)
    {
        _m["events"] = e;
        var ep = e.TryGetValue("endpoint", out var v) && v is string s ? s : "/ai2w/events";
        return Capability("events", new Dictionary<string, object?> { ["endpoint"] = ep });
    }

    public Manifest AgentService(Dictionary<string, object?> s) { _m["agent_service"] = s; return this; }

    public Dictionary<string, object?> Build() => _m;

    public string ToJson() => JsonSerializer.Serialize(_m, new JsonSerializerOptions { WriteIndented = true });

    private void Merge(string key, Dictionary<string, object?> t)
    {
        if (_m.GetValueOrDefault(key) is not Dictionary<string, object?> existing) { existing = new(); _m[key] = existing; }
        foreach (var kv in t) existing[kv.Key] = kv.Value;
    }
}
