using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ai2Web;

/// <summary>
/// AP2 (Agent Payments Protocol, Google - v0.2.0) merchant primitives.
///
/// AP2 is mandate-based: the merchant prices a buyer agent's Intent Mandate as a CartContents
/// (a W3C PaymentRequest, amounts in decimal major units) and digitally signs it into a
/// CartMandate - a short-lived guarantee of items and price - then settles a user-signed Payment
/// Mandate. This provides the reusable, app-agnostic core: build the mandate objects, sign a
/// CartContents as an RS256 JWT (cart_hash over the canonical contents), publish the public key as
/// a JWKS, verify a Cart Mandate, and parse a Payment Mandate. Signing uses
/// System.Security.Cryptography, so the SDK keeps zero third-party dependencies.
/// </summary>
public static class Ap2
{
    public const string ExtensionUri = "https://github.com/google-agentic-commerce/ap2/v1";
    public const string Version = "0.2.0";
    private const long DefaultTtl = 900;

    /// <summary>One cart line: a label, a unit price and a quantity (default 1).</summary>
    public readonly record struct LineItem(string Label, double UnitAmount, int Quantity = 1);

    /// <summary>The transports.ap2 advertisement to merge into a manifest.</summary>
    public static Dictionary<string, object?> Transport(Dictionary<string, object?>? overrides = null)
    {
        var t = new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["version"] = Version,
            ["extension"] = ExtensionUri,
            ["agent_card"] = "/ai2w/ap2/agent-card",
            ["cart"] = "/ai2w/ap2/cart",
            ["payment"] = "/ai2w/ap2/payment",
            ["jwks"] = "/ai2w/ap2/jwks",
        };
        if (overrides != null)
        {
            foreach (var kv in overrides) t[kv.Key] = kv.Value;
        }
        return t;
    }

    /// <summary>Build an AP2 IntentMandate (classic v0.2.0 shape).</summary>
    public static Dictionary<string, object?> IntentMandate(
        string description,
        IEnumerable<string>? merchants = null,
        IEnumerable<string>? skus = null,
        IEnumerable<Dictionary<string, object?>>? items = null,
        bool requiresRefundability = false,
        bool userCartConfirmationRequired = true,
        long expiresIn = DefaultTtl,
        long? now = null)
    {
        long ts = now ?? NowTs();
        var m = new Dictionary<string, object?>
        {
            ["natural_language_description"] = description,
            ["intent_expiry"] = Iso(ts + expiresIn),
            ["user_cart_confirmation_required"] = userCartConfirmationRequired,
        };
        if (merchants != null) { var l = merchants.ToList(); if (l.Count > 0) m["merchants"] = l; }
        if (skus != null) { var l = skus.ToList(); if (l.Count > 0) m["skus"] = l; }
        if (items != null) { var l = items.ToList(); if (l.Count > 0) m["items"] = l; }
        if (requiresRefundability) m["requires_refundability"] = true;
        return m;
    }

    /// <summary>AP2 PaymentCurrencyAmount: decimal major units, ISO 4217.</summary>
    public static Dictionary<string, object?> Amount(double value, string currency) =>
        new() { ["currency"] = currency.ToUpperInvariant(), ["value"] = Math.Round(value, 2, MidpointRounding.AwayFromZero) };

    /// <summary>Build a CartContents (W3C PaymentRequest) from line items.</summary>
    public static Dictionary<string, object?> CartContents(
        IEnumerable<LineItem> items,
        string currency,
        string merchantName,
        string? id = null,
        string? paymentDetailsId = null,
        long expiresIn = DefaultTtl,
        long? now = null)
    {
        long ts = now ?? NowTs();
        var display = new List<object?>();
        double total = 0;
        foreach (var it in items)
        {
            int qty = it.Quantity < 1 ? 1 : it.Quantity;
            double line = it.UnitAmount * qty;
            string label = qty > 1 ? $"{it.Label} x{qty}" : it.Label;
            display.Add(new Dictionary<string, object?> { ["label"] = label, ["amount"] = Amount(line, currency) });
            total += line;
        }
        return new Dictionary<string, object?>
        {
            ["id"] = id ?? "cart_" + RandHex(10),
            ["user_cart_confirmation_required"] = true,
            ["payment_request"] = new Dictionary<string, object?>
            {
                ["method_data"] = new List<object?> { new Dictionary<string, object?> { ["supported_methods"] = "card", ["data"] = new Dictionary<string, object?>() } },
                ["details"] = new Dictionary<string, object?>
                {
                    ["id"] = paymentDetailsId ?? "pr_" + RandHex(10),
                    ["display_items"] = display,
                    ["total"] = new Dictionary<string, object?> { ["label"] = "Total", ["amount"] = Amount(total, currency) },
                },
                ["options"] = new Dictionary<string, object?> { ["request_shipping"] = true },
            },
            ["cart_expiry"] = Iso(ts + expiresIn),
            ["merchant_name"] = merchantName,
        };
    }

    /// <summary>The merchant_authorization JWT (RS256) over the canonical CartContents.</summary>
    public static string SignCart(
        Dictionary<string, object?> contents,
        string privateKeyPem,
        string? kid = null,
        string? iss = null,
        string aud = "ap2-network",
        long expiresIn = DefaultTtl,
        long? now = null)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        long ts = now ?? NowTs();
        var p = rsa.ExportParameters(false);
        var header = new Dictionary<string, object?> { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = kid ?? Kid(p) };
        var claims = new Dictionary<string, object?>
        {
            ["iss"] = iss ?? (contents.TryGetValue("merchant_name", out var mn) ? mn : ""),
            ["sub"] = contents.GetValueOrDefault("id"),
            ["aud"] = aud,
            ["iat"] = ts,
            ["exp"] = ts + expiresIn,
            ["jti"] = RandHex(12),
            ["cart_hash"] = B64Url(SHA256.HashData(Canonical(contents))),
        };
        string signingInput = B64Url(Canonical(header)) + "." + B64Url(Canonical(claims));
        byte[] sig = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + B64Url(sig);
    }

    /// <summary>Sign CartContents into a CartMandate (contents + merchant_authorization).</summary>
    public static Dictionary<string, object?> CartMandate(
        Dictionary<string, object?> contents,
        string privateKeyPem,
        string? kid = null,
        string? iss = null,
        string aud = "ap2-network",
        long expiresIn = DefaultTtl,
        long? now = null) =>
        new()
        {
            ["contents"] = contents,
            ["merchant_authorization"] = SignCart(contents, privateKeyPem, kid, iss, aud, expiresIn, now),
        };

    /// <summary>JWKS publishing the cart-signing public key, for verifiers.</summary>
    public static Dictionary<string, object?> Jwks(string privateKeyPem, string? kid = null)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var p = rsa.ExportParameters(false);
        return new Dictionary<string, object?>
        {
            ["keys"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["kty"] = "RSA",
                    ["use"] = "sig",
                    ["alg"] = "RS256",
                    ["kid"] = kid ?? Kid(p),
                    ["n"] = B64Url(p.Modulus!),
                    ["e"] = B64Url(p.Exponent!),
                },
            },
        };
    }

    /// <summary>
    /// Verify a CartMandate's signature (against a public or private PEM) and its cart_hash
    /// binding, and that it has not expired.
    /// </summary>
    public static bool VerifyCartMandate(Dictionary<string, object?> mandate, string keyPem)
    {
        if (mandate.GetValueOrDefault("merchant_authorization") is not string jwt) return false;
        var parts = jwt.Split('.');
        if (parts.Length != 3) return false;

        using var rsa = RSA.Create();
        try { rsa.ImportFromPem(keyPem); } catch { return false; }

        byte[] sig;
        try { sig = B64UrlDecode(parts[2]); } catch { return false; }
        if (!rsa.VerifyData(Encoding.UTF8.GetBytes(parts[0] + "." + parts[1]), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            return false;
        }

        Dictionary<string, JsonElement>? claims;
        try { claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(B64UrlDecode(parts[1])); }
        catch { return false; }
        if (claims == null || !claims.TryGetValue("cart_hash", out var chEl) || chEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        string ch = chEl.GetString() ?? "";
        if (ch.Length == 0) return false;
        if (claims.TryGetValue("exp", out var expEl) && expEl.ValueKind == JsonValueKind.Number && NowTs() > expEl.GetInt64())
        {
            return false;
        }
        string expected = B64Url(SHA256.HashData(Canonical(mandate.GetValueOrDefault("contents"))));
        return string.Equals(ch, expected, StringComparison.Ordinal);
    }

    /// <summary>Extract the salient fields of a PaymentMandate for settlement.</summary>
    public static Dictionary<string, object?> PaymentDetails(Dictionary<string, object?> paymentMandate)
    {
        var c = paymentMandate.GetValueOrDefault("payment_mandate_contents") as Dictionary<string, object?> ?? new();
        var resp = c.GetValueOrDefault("payment_response") as Dictionary<string, object?> ?? new();
        var total = c.GetValueOrDefault("payment_details_total") as Dictionary<string, object?>;
        return new Dictionary<string, object?>
        {
            ["payment_mandate_id"] = c.GetValueOrDefault("payment_mandate_id"),
            ["payment_details_id"] = c.GetValueOrDefault("payment_details_id"),
            ["total"] = total?.GetValueOrDefault("amount"),
            ["method"] = resp.GetValueOrDefault("method_name"),
            ["payer_email"] = resp.GetValueOrDefault("payer_email"),
            ["payer_name"] = resp.GetValueOrDefault("payer_name"),
        };
    }

    // --- helpers ---

    private static byte[] Canonical(object? v) => JsonSerializer.SerializeToUtf8Bytes(v);

    private static long NowTs() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string Iso(long ts) =>
        DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss+00:00");

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] B64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }

    private static string RandHex(int n) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(n)).ToLowerInvariant();

    private static string Kid(RSAParameters p)
    {
        byte[] material = new byte[p.Modulus!.Length + p.Exponent!.Length];
        Buffer.BlockCopy(p.Modulus, 0, material, 0, p.Modulus.Length);
        Buffer.BlockCopy(p.Exponent, 0, material, p.Modulus.Length, p.Exponent.Length);
        return Convert.ToHexString(SHA256.HashData(material)).ToLowerInvariant()[..16];
    }
}
