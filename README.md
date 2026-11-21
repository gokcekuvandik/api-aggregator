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
  Aggregator.ArchitectureTests  dependency rule, Domain purity
```

The dependency rule is enforced, not just documented: `Aggregator.ArchitectureTests` fails when a layer depends outward (ArchUnitNET) or when Domain references an assembly that does not load from the shared framework.

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

Still to check when they are added: Scalar (OpenAPI 3.2), WireMock.Net, Verify, BenchmarkDotNet, Stryker.NET.
