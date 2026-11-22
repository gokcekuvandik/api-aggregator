# Statistics store benchmarks

Each operation records 10,000 requests for one external API, split evenly across `Writers` parallel writers, then reads one result.
`Lock` is the store the service uses (`ApiStatistics`: one `System.Threading.Lock`, per-bucket totals and the 5-minute ring buffer).
`Interlocked` and `Channel` exist only in the benchmark project, and they keep less: per-bucket totals only, with no window.

Run on 2026-11-22 with:

```bash
dotnet run -c Release --project benchmarks/Aggregator.Benchmarks -- --filter "*"
```

```
.NET 11.0.0 (11.0.26.53018), X64 RyuJIT x86-64-v3
Job=ShortRun  Toolchain=InProcessEmitToolchain  IterationCount=3  LaunchCount=1  WarmupCount=3
```

| Method                                 | Writers | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|--------------------------------------- |-------- |-----------:|------------:|----------:|------:|--------:|--------:|----------:|------------:|
| 'Lock (store in use)'                  | 1       |   247.4 us |    31.12 us |   1.71 us |  1.00 |    0.01 |       - |   3.08 KB |        1.00 |
| 'Interlocked (no consistent snapshot)' | 1       |   168.0 us |   115.21 us |   6.32 us |  0.68 |    0.02 |  0.2441 |   1.84 KB |        0.60 |
| 'Channel (single-writer actor)'        | 1       | 1,943.0 us | 3,672.92 us | 201.33 us |  7.85 |    0.71 |  1.9531 |  21.83 KB |        7.09 |
|                                        |         |            |             |           |       |         |         |           |             |
| 'Lock (store in use)'                  | 4       | 2,006.1 us | 1,360.83 us |  74.59 us |  1.00 |    0.05 |       - |   3.86 KB |        1.00 |
| 'Interlocked (no consistent snapshot)' | 4       |   825.2 us | 1,782.63 us |  97.71 us |  0.41 |    0.04 |       - |   2.49 KB |        0.65 |
| 'Channel (single-writer actor)'        | 4       | 2,472.5 us | 3,190.64 us | 174.89 us |  1.23 |    0.09 |       - |  19.22 KB |        4.98 |
|                                        |         |            |             |           |       |         |         |           |             |
| 'Lock (store in use)'                  | 16      | 1,782.8 us | 1,119.46 us |  61.36 us |  1.00 |    0.04 |       - |   6.18 KB |        1.00 |
| 'Interlocked (no consistent snapshot)' | 16      |   826.5 us |   479.02 us |  26.26 us |  0.46 |    0.02 |       - |   3.72 KB |        0.60 |
| 'Channel (single-writer actor)'        | 16      | 2,720.3 us | 1,834.48 us | 100.55 us |  1.53 |    0.07 | 11.7188 |  65.91 KB |       10.66 |

## Reading

- **Lock:** under the worst contention measured (16 writers hammering one API with no other work), it costs about 0.2 µs per record, allocation-free per record.
- **Real load:** the service records one entry per upstream HTTP call, which takes tens to hundreds of milliseconds. The lock is therefore several orders of magnitude below anything a request would notice.
- **Interlocked:** about twice as fast under contention. A reader can still see a bucket's count without its matching total, though, and it has no ring buffer: resetting a slice takes several fields at once, and atomic adds cannot do that.
- **Channel:** slower at every writer count, and it allocates more. A snapshot would also have to be requested through the actor, and it could lag behind writes.
- **Conclusion:** the lock is kept. It is the only option measured that gives a consistent snapshot and the window at a cost that does not matter for this workload.
- **Caveat:** `ShortRun` uses three iterations, so the error columns are wide. Compare ratios, not absolute times.
