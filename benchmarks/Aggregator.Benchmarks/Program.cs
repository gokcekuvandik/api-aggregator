using Aggregator.Benchmarks;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// In-process, so the benchmark runs on the SDK the solution targets without BenchmarkDotNet generating a project for it.
var config = ManualConfig.CreateEmpty()
    .AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance))
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddColumnProvider(DefaultColumnProviders.Instance)
    .AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default)
    .AddExporter(MarkdownExporter.GitHub);

BenchmarkSwitcher.FromAssembly(typeof(StatisticsStoreBenchmarks).Assembly).Run(args, config);
