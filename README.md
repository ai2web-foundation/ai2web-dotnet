<div align="center">
  <a href="https://ai2web.dev">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/ai2web-foundation/.github/main/profile/ai2web-logo-white.svg">
      <img alt="AI2Web" src="https://raw.githubusercontent.com/ai2web-foundation/.github/main/profile/ai2web-logo-black.svg" width="200">
    </picture>
  </a>
</div>

# AI2Web .NET SDK (`Ai2Web`)

[![AI2Web on Launchpadly - Product of the Week (Gold)](https://launchpadly.co/embed/badges/startup/ai2web.svg?variant=product-week-gold)](https://launchpadly.co/startup/ai2web?ref=badge)

[![CI](https://github.com/ai2web-foundation/ai2web-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/ai2web-foundation/ai2web-dotnet/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Ai2Web)](https://www.nuget.org/packages/Ai2Web)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Ai2Web)](https://www.nuget.org/packages/Ai2Web)

The .NET reference implementation of the [AI2Web protocol](https://github.com/ai2web-foundation/ai2web-spec). Mirrors `@ai2web/core`.

```bash
dotnet add package Ai2Web
```

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
