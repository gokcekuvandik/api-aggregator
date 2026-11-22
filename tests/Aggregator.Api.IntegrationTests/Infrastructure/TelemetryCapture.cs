using System.Collections;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Exports a host's spans and metrics to memory. Listeners are process-wide, so spans are read back by the trace id this capture sends as <c>traceparent</c>.
/// </summary>
public sealed class TelemetryCapture
{
    private readonly LockedCollection<Activity> _activities = new();
    private readonly LockedCollection<Metric> _metrics = new();

    public ActivityTraceId TraceId { get; } = ActivityTraceId.CreateRandom();

    public void AddTo(IServiceCollection services)
    {
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(_activities));
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(_metrics));
    }

    /// <summary>Makes every request of <paramref name="client"/> part of this capture's trace.</summary>
    public HttpClient Traced(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Add("traceparent", $"00-{TraceId}-{ActivitySpanId.CreateRandom()}-01");
        return client;
    }

    /// <summary>The finished spans of this capture's trace.</summary>
    public IReadOnlyList<Activity> Spans => [.. _activities.Snapshot().Where(activity => activity.TraceId == TraceId)];

    /// <summary>
    /// The finished spans of this capture's trace once one of them matches <paramref name="until"/>.
    /// The server span ends just after the response reaches the client, so tests that read it wait here.
    /// </summary>
    public async Task<IReadOnlyList<Activity>> SpansWhenAsync(Func<Activity, bool> until)
    {
        ArgumentNullException.ThrowIfNull(until);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!Spans.Any(until))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new ShouldAssertException($"No span matched within 5 seconds; the trace had: {string.Join(", ", Spans.Select(span => $"{span.Kind} {span.DisplayName}"))}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return Spans;
    }

    /// <summary>The finished spans of this capture's trace once the API's aggregation server span has ended.</summary>
    public Task<IReadOnlyList<Activity>> AggregationSpansAsync() =>
        SpansWhenAsync(span => span.Kind == ActivityKind.Server && Equals(span.GetTagItem("url.path"), ApiRoutes.Aggregation));

    /// <summary>Collects the host's metrics now and returns the names of every exported instrument.</summary>
    public IReadOnlySet<string> MetricNames(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GetRequiredService<MeterProvider>().ForceFlush();
        return _metrics.Snapshot().Select(metric => metric.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Spans end on many threads at once, so the exporter's collection takes a lock.</summary>
    private sealed class LockedCollection<T> : ICollection<T>
    {
        private readonly List<T> _items = [];

        public int Count
        {
            get
            {
                lock (_items)
                {
                    return _items.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public List<T> Snapshot()
        {
            lock (_items)
            {
                return [.. _items];
            }
        }

        public void Add(T item)
        {
            lock (_items)
            {
                _items.Add(item);
            }
        }

        public void Clear()
        {
            lock (_items)
            {
                _items.Clear();
            }
        }

        public bool Contains(T item) => Snapshot().Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => Snapshot().CopyTo(array, arrayIndex);

        public bool Remove(T item)
        {
            lock (_items)
            {
                return _items.Remove(item);
            }
        }

        public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
