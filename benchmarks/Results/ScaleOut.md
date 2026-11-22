# Scale-out: upstream calls per replica count

`benchmarks/k6/scale-out.js` warms three fixed aggregation queries once, then replays them at 5 requests a second for 30 seconds through the Aspire proxy, which spreads them over the replicas.
In teardown it sums `totalRequests` (outbound upstream calls) and `cacheHits` from every replica's `/api/v1/statistics`.
One query costs four upstream calls when cold: news 1, repositories 1, weather 2 (geocoding and forecast).

Run on 2026-11-22 with k6 1.4.2 (`grafana/k6:1.4.2`), Redis 8.10 from `Aspire.Hosting.Redis` 17.0.0, keyless providers (Open-Meteo, Hacker News, GitHub without a token).
Each row is a fresh AppHost, so every run starts with empty caches.

| Topology | AppHost arguments | Requests | Checks passed | Upstream calls (news / repositories / weather) | Cache hits |
|---|---|---:|---:|---:|---:|
| 1 replica, L1 only | `--ScaleOut:Enabled=false` | 154 | 100% | **12** (3 / 3 / 6) | 150 per source |
| 3 replicas, L1 only | `--ScaleOut:Enabled=true --ScaleOut:Redis=false` | 157 | 100% | **36** (9 / 9 / 18) | 145 per source |
| 3 replicas, Redis L2 | `--ScaleOut:Enabled=true` | 157 | 100% | **12** (3 / 3 / 6) | 151 per source |

Without a shared second level every replica fetches each query once, so upstream calls grow with the replica count.
With Redis the first replica's answer serves the others, and the count stays at one replica's.
The warm-up runs before the load so replicas never race on a cold key (see the README on concurrent misses).

## Reproduce

```bash
TOKEN=$(dotnet user-jwts create --project src/Aggregator.Api --scope aggregation:read --scope stats:read --output token)
dotnet run --project src/Aggregator.AppHost --launch-profile scaled          # add -- --ScaleOut:Redis=false for the L1-only row
# Each api replica's port is shown in the Aspire dashboard.
docker run --rm -i -e TOKEN="$TOKEN" \
  -e BASE_URL=http://host.docker.internal:5181 \
  -e REPLICA_URLS=http://host.docker.internal:<port1>,http://host.docker.internal:<port2>,http://host.docker.internal:<port3> \
  -v "$PWD/benchmarks/k6:/scripts:ro" grafana/k6:1.4.2 run /scripts/scale-out.js
```

Leave about a minute between runs: unauthenticated GitHub search allows 10 calls a minute, and the L1-only run with three replicas makes nine.
