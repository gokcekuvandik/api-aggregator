using System.Diagnostics.Metrics;
using Aggregator.Application.Statistics;
using Aggregator.Application.Telemetry;
using Aggregator.Domain.Statistics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>
/// Checks this instance's statistics once per interval and reports an external API whose recent average is well above its overall average.
/// It logs and counts only when an API turns anomalous or recovers; a window with too few samples leaves the last state unchanged.
/// </summary>
internal sealed partial class AnomalyDetector : BackgroundService
{
    public const string AnomaliesName = "aggregator.source.anomalies";

    private readonly IStatisticsReader _reader;
    private readonly AnomalyRule _rule;
    private readonly TimeSpan _interval;
    private readonly ILogger<AnomalyDetector> _logger;
    private readonly Counter<long> _anomalies;
    private readonly HashSet<UpstreamApi> _anomalous = [];

    // Created here rather than in the loop, which BackgroundService starts on another thread, so the first check is one interval after the host resolves the service.
    private readonly PeriodicTimer _timer;

    public AnomalyDetector(
        IStatisticsReader reader,
        IOptions<StatisticsOptions> options,
        TimeProvider time,
        IMeterFactory meterFactory,
        ILogger<AnomalyDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _reader = reader;
        _rule = options.Value.Anomalies.Rule;
        _interval = options.Value.Anomalies.Interval;
        _logger = logger;
        _anomalies = meterFactory.Create(AggregatorDiagnostics.Name)
            .CreateCounter<long>(AnomaliesName, unit: "{anomaly}", description: "Times an external API turned anomalously slow.");
        _timer = new PeriodicTimer(_interval, time);
    }

    /// <summary>Runs one check; only the background loop calls it outside tests, so the state needs no lock.</summary>
    internal void CheckOnce()
    {
        foreach (var snapshot in _reader.GetSnapshots())
        {
            var (api, recent, overall) = (snapshot.Api, snapshot.Recent, snapshot.AverageDuration);
            var provider = SourceTelemetry.ProviderTag(api);
            switch (_rule.Evaluate(recent.AverageDuration, overall, recent.Count))
            {
                case AnomalyVerdict.Anomalous when !_anomalous.Contains(api):
                    var ratio = Math.Round(recent.AverageDuration / overall, 2);
                    _anomalies.Add(1, SourceTelemetry.ApiTags(api));
                    LogAnomaly(api.Source, provider, recent.AverageDuration.TotalMilliseconds, recent.Length, overall.TotalMilliseconds, ratio, recent.Count);
                    _anomalous.Add(api);
                    break;
                case AnomalyVerdict.Normal when _anomalous.Remove(api):
                    LogRecovered(api.Source, provider, recent.AverageDuration.TotalMilliseconds, recent.Length, overall.TotalMilliseconds);
                    break;
            }
        }
    }

    public override void Dispose()
    {
        _timer.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(stoppingToken))
            {
                CheckSafely();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>A failed check must not stop the host, so it is logged and the next tick tries again.</summary>
#pragma warning disable CA1031
    private void CheckSafely()
    {
        try
        {
            CheckOnce();
        }
        catch (Exception exception)
        {
            LogCheckFailed(exception, _interval);
        }
    }
#pragma warning restore CA1031

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Source {Source} ({Provider}) is slower than usual: {WindowAverageMs} ms on average over the last {Window} against {OverallAverageMs} ms overall, {Ratio}x over {SampleCount} requests")]
    private partial void LogAnomaly(string source, string provider, double windowAverageMs, TimeSpan window, double overallAverageMs, double ratio, long sampleCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Source {Source} ({Provider}) is back to normal: {WindowAverageMs} ms on average over the last {Window} against {OverallAverageMs} ms overall")]
    private partial void LogRecovered(string source, string provider, double windowAverageMs, TimeSpan window, double overallAverageMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "The anomaly check failed; the next check runs in {Interval}")]
    private partial void LogCheckFailed(Exception exception, TimeSpan interval);
}
