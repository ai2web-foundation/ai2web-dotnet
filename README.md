# AI2Web .NET SDK (`Ai2Web`)

[![CI](https://github.com/ai2web-foundation/ai2web-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/ai2web-foundation/ai2web-dotnet/actions/workflows/ci.yml)

The .NET reference implementation of the [AI2Web protocol](https://github.com/ai2web-foundation/ai2web-spec). Mirrors `@ai2web/core`.

```csharp
using Ai2Web;

var manifest = Manifest.ForSite("Example Store", "https://example.com", "ecommerce")
    .Capability("content")
    .Capability("commerce", new Dictionary<string, object?> { ["endpoint"] = "/ai2w/products", ["checkout"] = true })
    .Transports(new Dictionary<string, object?> {
        ["mcp"] = new Dictionary<string, object?> { ["enabled"] = true, ["endpoint"] = "/ai2w/mcp" },
        ["rest"] = new Dictionary<string, object?> { ["enabled"] = true } })
    .Auth(new Dictionary<string, object?> { ["methods"] = new List<object?> { "none", "oauth2" }, ["oauth2"] = new Dictionary<string, object?> { ["pkce"] = true } })
    .Consent(new Dictionary<string, object?> { ["requires_user_approval_for"] = new List<object?> { "purchase" } })
    .Contact(new Dictionary<string, object?> { ["support"] = "help@example.com" })
    .Build();

Result r = Validator.Validate(manifest);        // r.Score, r.Tier, r.Valid, ...

// Serve every AI2Web route (framework-agnostic):
Response res = Server.Handle(manifest, method, path, body, origin);
```

## API
- `Manifest.ForSite(...)` - fluent capability-model builder.
- `Validator.Validate(...)` - AI Readiness scoring (spec §9/§11).
- `Negotiator.Negotiate(...)` - capability negotiation (spec §5).
- `Server.Handle(...)` - framework-agnostic router.
- `Safety.IsSafePublicUrl` / `AssertSafePublicUrl` / `SameOrigin` - SSRF guard.
- `Json.Parse` / `Json.ToObject` - JSON ⇄ the plain object model.

## Test
```bash
dotnet run --project Ai2Web.Tests     # includes the shared conformance contract
```

Requires **.NET 8+**. No external dependencies.

## Licence
MIT.
