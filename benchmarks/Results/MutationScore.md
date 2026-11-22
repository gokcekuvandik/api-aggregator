# Mutation score (Stryker.NET)

Stryker.NET 5.1.0 changes one piece of production code at a time and reruns the unit tests. A mutant that no test notices "survives".
The score is killed (plus timed out) over killed, timed out, survived and not covered. Mutants that do not compile, and those Stryker ignores, are left out.

Run on 2026-11-22 with SDK 11.0.100 and the Microsoft.Testing.Platform runner (`test-runner: mtp` in each `stryker-config.json`).

| Project | Score | Killed | Timeout | Survived | No coverage | Time |
|---|---:|---:|---:|---:|---:|---:|
| Aggregator.Domain | **95.8%** | 46 | 0 | 2 | 0 | 19 s |
| Aggregator.Application | **80.7%** | 183 | 1 | 42 | 2 | 46 s |
| Aggregator.Infrastructure | **84.2%** | 474 | 0 | 81 | 8 | 1 min 57 s |

Application scored 76.3% on the first run. Its survivors pointed at real gaps, and the tests added for them raised the score:
- the source span's `failure` tag, its error status naming the exception, and its `cancelled` tag;
- the unit (`s`) and bucket boundaries of `aggregator.aggregation.duration`;
- the paged path rejecting an invalid query before calling a source;
- `AddApplication` and its startup check, and an unknown sort direction;
- the cache key exactly at `MaxLength` and one over it.

In Infrastructure, 8 survivors were the edges of the WMO weather-code ranges (`>= 51` → `> 51` and so on). A boundary-value theory now covers each edge and its outside neighbour, and all of them are killed.

What survives, and why it is left:
- **Argument guards** (`ThrowIfNull` and similar): 33 in Infrastructure and about 20 in Application. The callers are nullable-annotated, so removing a guard changes nothing a test can reach without passing `null!`.
- **Message text:** exception messages, metric descriptions and log text. Tests assert the exception type and parameter, not the wording.
- **Equivalent mutants**, for example `skip >= count` → `skip > count` in paging (skipping past the end is empty either way), and `age > 0` → `age >= 0` when clamping a cache age to zero.
- **Infrastructure:** read per file in the HTML report. The largest group is `AnomalyDetector` (9: five constructor guards, the counter's unit and description, and the two `Dispose` calls).

The score moves by about two points between identical runs (Application 79.8–81.6%). Stryker compiles mutants in groups, rolls back the ones that break the build, and the set it rolls back is not the same each run. That is why the Infrastructure score stayed at 84.2% after the WMO mutants were killed: in that run, other mutants that had not compiled before were tested and survived.

## Reproduce

```bash
dotnet tool restore
cd tests/Aggregator.Application.UnitTests     # or Aggregator.Domain.UnitTests / Aggregator.Infrastructure.UnitTests
dotnet stryker                                 # writes StrykerOutput/<timestamp>/reports/mutation-report.html
```
