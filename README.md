# AgileActors API Aggregator

ASP.NET Core 11 service that fans out to several external APIs, merges their results and exposes them through a single endpoint.

## Solution layout

Clean Architecture, ports-and-adapters rule: dependencies point inward.

```
src/
  Aggregator.Domain           entities, value objects, domain rules        → (nothing)
  Aggregator.Application      ports and use cases                          → Domain
  Aggregator.Infrastructure   source adapters, HTTP clients, cache, stats  → Application, Domain
  Aggregator.Api              endpoints, auth, OpenAPI, composition root   → all of the above
  Aggregator.ServiceDefaults  shared telemetry, health, resilience defaults
  Aggregator.AppHost          Aspire orchestration
tests/
  Aggregator.Domain.UnitTests
  Aggregator.Application.UnitTests
  Aggregator.Infrastructure.UnitTests
  Aggregator.Api.IntegrationTests
  Aggregator.ArchitectureTests  dependency rule, Domain purity, endpoint conventions
```

The dependency rule is enforced, not just documented: `Aggregator.ArchitectureTests` fails when a layer depends outward (ArchUnitNET) or when Domain references an assembly that does not load from the shared framework.

## HTTP API

Minimal APIs, versioned in the URL. Each feature is one sealed `IEndpoint` class under `Aggregator.Api.Endpoints`, found by an assembly scan, so a new endpoint needs no wiring.

| Method and path | Purpose | Responses |
|---|---|---|
| `GET /api/v1/aggregation?page=1&pageSize=20` | Items from every source, merged and paged | 200, 400, 503 |
| `GET /api/v1/sources` | Registered sources and their categories | 200 |
| `GET /api/v1/statistics` | Request statistics per source | 200 |
| `GET /health/live`, `GET /health/ready` | Liveness and readiness probes (`Healthy` / `Unhealthy`) | 200, 503 |
| `GET /openapi/v1.json` | OpenAPI 3.2 document; Scalar UI at `/scalar` in Development | 200 |

`page` must be at least 1; `pageSize` must be between 1 and 100 (default 20). An unknown version such as `/api/v2/…` is 404.

### Aggregation response

```json
{
  "items": [
    { "id": "news-1", "source": "news", "category": "news", "title": "…", "summary": "…",
      "url": "https://…", "publishedAt": "2026-11-21T12:00:00+00:00" }
  ],
  "sources": [
    { "name": "news",         "status": "Success",  "durationMs": 41.2, "error": null,           "ageSeconds": null },
    { "name": "weather",      "status": "Fallback", "durationMs": 3.1,  "error": null,           "ageSeconds": 90 },
    { "name": "repositories", "status": "Failed",   "durationMs": 5000, "error": "…unavailable", "ageSeconds": null }
  ],
  "paging": { "page": 1, "pageSize": 20, "totalCount": 3 }
}
```

**A 200 can be degraded.** The status is 200 whenever at least one source produced data, live (`Success`), from cache (`Cached`) or as last-known-good data (`Fallback`, with its age). Read `sources[]` to see which sources failed. Only when every source fails, with no fallback, is the answer 503.

### Errors

Every error is an RFC 9457 `application/problem+json` body with a `traceId` that matches the distributed trace. A 503 also carries `sources[]`. Validation errors (400) are keyed by the query parameter name (`pageSize`). A 500 never contains exception details.

### Contract tests

`tests/Aggregator.Api.IntegrationTests/Snapshots` pins the OpenAPI document and the 200, 503 and 400 bodies as `*.verified.json`. A contract change fails the test and writes a `*.received.json` next to it: review it and rename it to accept, or run with `AGGREGATOR_UPDATE_SNAPSHOTS=1` to overwrite all snapshots.

## Build and run

Requires the .NET 11 SDK (pinned in `global.json`).

```bash
dotnet build                                   # warnings are errors (Directory.Build.props)
dotnet test
dotnet run --project src/Aggregator.AppHost     # Aspire dashboard + API
```

Package versions are managed centrally in `Directory.Packages.props`. Shared build settings (net11.0, nullable, warnings as errors) live in `Directory.Build.props`.

## Package compatibility on net11.0

Checked on 2026-11-21 with SDK 11.0.100: restore and build produce no warnings.

| Package | Version | Note |
|---|---|---|
| Aspire.Hosting.AppHost / .Dotnet | 17.0.0 | `AddProject<T>` is obsolete; use `AddDotnetProject(name, path)` |
| Microsoft.Extensions.Http.Resilience / ServiceDiscovery | 11.0.1 | |
| OpenTelemetry.* | 1.21.0 | |
| xunit.v3.mtp-v2 | 4.0.3 | runs on Microsoft.Testing.Platform (`global.json` → `test.runner`) |
| Shouldly | 4.3.0 | |
| NSubstitute | 6.2.0 | |
| TngTech.ArchUnitNET.xUnitV3 | 0.13.5 | |
| Microsoft.AspNetCore.Mvc.Testing | 11.0.0 | |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | `dotnet test -- --coverage` |
| Microsoft.AspNetCore.OpenApi | 11.0.0 | generates OpenAPI 3.2; built on Microsoft.OpenApi 3.x |
| Asp.Versioning.Http / .Mvc.ApiExplorer | 10.2.3 / 10.2.1 | net10.0 builds, work on net11.0 |
| Scalar.AspNetCore | 3.1.1 | reads the OpenAPI 3.2 document |

Not used, and why:

- **Asp.Versioning.OpenApi 10.2.3** pins Microsoft.OpenApi 2.x, which conflicts with Microsoft.AspNetCore.OpenApi 11 (NU1107). The built-in generator reads the versioned ApiExplorer groups instead; analyzer warnings AV0029 / AV0030 are suppressed in `Aggregator.Api.csproj` with that reason.
- **Verify 33.7.1** fails the build (SponsorCheck SC021) unless the consuming project declares a maintenance-fee status. Snapshots use a small in-house helper (`JsonSnapshot`) instead.
- **Microsoft.Extensions.DependencyInjection.Abstractions** is available on net11.0 without a reference (NU1510).

Still to check when they are added: WireMock.Net, BenchmarkDotNet, Stryker.NET.
