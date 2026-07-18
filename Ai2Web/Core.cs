using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Ai2Web;

/// <summary>Internal helpers shared by the validator/negotiator/server.</summary>
internal static class H
{
    public static Dictionary<string, object?>? Map(object? v) => v as Dictionary<string, object?>;
    public static List<object?>? List(object? v) => v as List<object?>;
    public static string Str(object? v) => v as string ?? "";

    public static bool Has(object? v)
    {
        if (v is bool b) return b;
        var m = Map(v);
        return m != null && m.TryGetValue("enabled", out var e) && e is bool eb && eb;
    }

    public static bool BoolIn(object? v, string key)
    {
        var m = Map(v);
        return m != null && m.TryGetValue(key, out var x) && x is bool b && b;
    }

    public static bool ContainsStr(List<object?>? list, string target)
        => list != null && list.Any(x => Str(x) == target);
}

/// <summary>SSRF guard. Parity with @ai2web/core safety.</summary>
public static class Safety
{
    public static bool IsSafePublicUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != "https" && u.Scheme != "http") return false;
        var host = u.Host.ToLowerInvariant();
        if (host.Length == 0) return false;

        if (IPAddress.TryParse(host.Trim('[', ']'), out var ip))
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();   // treat ::ffff:a.b.c.d as a.b.c.d
            if (IPAddress.IsLoopback(ip)) return false;
            var b = ip.GetAddressBytes();
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                if (b[0] is 0 or 10) return false;
                if (b[0] == 169 && b[1] == 254) return false;                 // link-local + metadata
                if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;    // private
                if (b[0] == 192 && b[1] == 168) return false;                 // private
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;   // CGNAT
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal) return false;
                if ((b[0] & 0xfe) == 0xfc) return false;                      // ULA fc00::/7
            }
            return true;
        }

        if (host == "localhost" || host.EndsWith(".localhost")) return false;

        // Alternative IPv4 encodings that did not normalise to a parseable IP but a resolver may
        // still map to a private address: hex (0x7f000001 / 0x7f.0.0.1), decimal integer
        // (2130706433), octal, and short forms (127.1).
        foreach (var label in host.Split('.'))
            if (label.StartsWith("0x")) return false;
        if (!host.Any(c => c is >= 'a' and <= 'z')) return false;
        return true;
    }

    public static string AssertSafePublicUrl(string raw)
    {
        if (!IsSafePublicUrl(raw))
            throw new InvalidOperationException($"ai2w: refusing to fetch non-public or unsafe URL: {raw}");
        return raw;
    }

    public static bool SameOrigin(string a, string b)
    {
        if (!Uri.TryCreate(a, UriKind.Absolute, out var ua) || !Uri.TryCreate(b, UriKind.Absolute, out var ub))
            return false;
        return ua.Scheme == ub.Scheme && ua.Host == ub.Host && ua.Port == ub.Port;
    }
}

/// <summary>Converts a parsed JsonElement into the plain object model (Dictionary/List/primitives)
/// the validator operates on - so JSON manifests and builder output share one shape.</summary>
public static class Json
{
    public static object? ToObject(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ToObject(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(ToObject).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => (object)e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static Dictionary<string, object?> Parse(string json)
        => (Dictionary<string, object?>)ToObject(JsonDocument.Parse(json).RootElement)!;
}
