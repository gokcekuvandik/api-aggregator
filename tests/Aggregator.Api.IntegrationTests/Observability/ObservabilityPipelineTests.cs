using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Aggregator.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;

namespace Aggregator.Api.IntegrationTests.Observability;

/// <summary>End to end: the trace of one aggregation, the resilience telemetry, what spans never carry, and log correlation.</summary>
public sealed class ObservabilityPipelineTests : IClassFixture<AggregatorApiFactory>, IClassFixture<UpstreamServer>
{
    private const string WeatherKey = "owm-secret-5d1e";
    private const string NewsKey = "newsapi-secret-9b07";
    private const string SearchText = "secret-search";
    private const string SearchCity = "Atlantis";
    private const string Search = $"?q={SearchText}&city={SearchCity}";

    private readonly AggregatorApiFactory _factory;
    private readonly UpstreamServer _upstream;

    public ObservabilityPipelineTests(AggregatorApiFactory factory, UpstreamServer upstream)
    {
        _factory = factory;
        _upstream = upstream;
        _upstream.Server.Reset();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trace_ShouldNestServerAggregateSourceAndClientSpans_WhenAllSourcesAreFetched(bool cached)
    {
        _upstream.StubKeylessSources();
        var capture = new TelemetryCapture();
        var (client, _) = _factory.CreateWithUpstreams(cached ? _upstream.Settings() : _upstream.SettingsWithoutCache(), capture.AddTo);
        using var traced = capture.Traced(client);

        await AggregateAsync(traced, string.Empty);

        var spans = await capture.AggregationSpansAsync();
        var aggregate = spans.Where(span => span.OperationName == "aggregate").ShouldHaveSingleItem();

        // The in-process WireMock upstream joins the trace too, so the API's server span is found as the aggregate span's parent.
        var server = spans.Where(span => span.SpanId == aggregate.ParentSpanId).ShouldHaveSingleItem();
        server.Kind.ShouldBe(ActivityKind.Server);
        server.GetTagItem("url.path").ShouldBe(ApiRoutes.Aggregation);
        var sources = spans.Where(span => span.OperationName == "aggregate.source").ToList();
        sources.Select(span => span.GetTagItem("aggregator.source")).ShouldBe(["weather", "news", "repositories"], ignoreOrder: true);
        sources.ShouldAllBe(span => span.ParentSpanId == aggregate.SpanId);
        var sourceSpanIds = sources.Select(span => span.SpanId).ToHashSet();
        var clients = spans.Where(span => span.Kind == ActivityKind.Client).ToList();
        clients.ShouldNotBeEmpty();
        clients.ShouldAllBe(span => sourceSpanIds.Contains(span.ParentSpanId) || spans.Any(parent => parent.SpanId == span.ParentSpanId && parent.Kind == ActivityKind.Client));
    }

    [Fact]
    public async Task Trace_ShouldCarryAFallbackEvent_WhenTheKeyedPrimaryFailsAndTheSecondaryStandsIn()
    {
        _upstream.StubKeylessSources();
        _upstream.StubStatus(UpstreamServer.NewsApiPath, 500);
        var capture = new TelemetryCapture();
        var (client, _) = _factory.CreateWithUpstreams(KeyedSettings(), capture.AddTo);
        using var traced = capture.Traced(client);

        await AggregateAsync(traced, "?sources=news");

        var news = capture.Spans.Where(span => span.OperationName == "aggregate.source").ShouldHaveSingleItem();
        news.GetTagItem("aggregator.result").ShouldBe("fallback");
        var fallback = news.Events.Where(e => e.Name == "aggregator.fallback").ShouldHaveSingleItem();
        fallback.Tags.ShouldContain(new KeyValuePair<string, object?>("aggregator.fallback.result", "secondary"));
    }

    [Fact]
    public void CircuitState_ShouldReadClosedForEverySource_WhenNoRequestHasBeenMade()
    {
        var (client, services) = _factory.CreateWithUpstreams(_upstream.SettingsWithoutCache(), _ => { });
        using var _ = client;
        using var states = new MetricCollector<int>(services.GetRequiredService<IMeterFactory>(), AggregatorDiagnostics.Name, "aggregator.source.circuit_state");

        states.RecordObservableInstruments();

        var snapshot = states.GetMeasurementSnapshot();
        snapshot.ShouldAllBe(state => state.Value == 0);
        snapshot.Where(state => (string?)state.Tags["provider"] == "primary").Select(state => (string?)state.Tags["source"])
            .ShouldBe(["weather", "news", "repositories"], ignoreOrder: true);
    }

    [Fact]
    public async Task CircuitState_ShouldReadOpenAndTheTraceShouldRecordIt_WhenAnUpstreamKeepsFailing()
    {
        _upstream.StubKeylessSources();
        _upstream.StubStatus(UpstreamServer.GitHubPath, 500);
        var capture = new TelemetryCapture();
        var (client, services) = _factory.CreateWithUpstreams(_upstream.SettingsWithoutCache(), capture.AddTo);
        using var traced = capture.Traced(client);
        using var states = new MetricCollector<int>(services.GetRequiredService<IMeterFactory>(), AggregatorDiagnostics.Name, "aggregator.source.circuit_state");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await AggregateAsync(traced, "?sources=repositories", HttpStatusCode.ServiceUnavailable);
        }

        states.RecordObservableInstruments();
        states.GetMeasurementSnapshot().ShouldContain(state => (string?)state.Tags["source"] == "repositories" && state.Value == 2);
        capture.Spans.SelectMany(span => span.Events).ShouldContain(e => e.Name == "aggregator.circuit.opened");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Telemetry_ShouldNeverCarryApiKeysOrTheCallersSearch_WhenKeyedProvidersAreCalled(bool keyedUpstreamsFail)
    {
        _upstream.StubJson(UpstreamServer.GitHubPath, "github-search.json");
        _upstream.StubJson(UpstreamServer.GeocodingPath, "open-meteo-geocoding.json");
        if (keyedUpstreamsFail)
        {
            _upstream.StubStatus(UpstreamServer.OpenWeatherMapPath, 500);
            _upstream.StubStatus(UpstreamServer.NewsApiPath, 500);
        }
        else
        {
            _upstream.StubJson(UpstreamServer.OpenWeatherMapPath, "openweathermap-forecast.json");
            _upstream.StubJson(UpstreamServer.NewsApiPath, "newsapi-everything.json");
        }

        var capture = new TelemetryCapture();
        var logs = new CapturingLoggerProvider();
        var (client, _) = _factory.CreateWithUpstreams(KeyedSettings(), capture.AddTo, logs);
        using var traced = capture.Traced(client);

        await AggregateAsync(traced, Search);

        var spans = await capture.AggregationSpansAsync();
        spans.ShouldContain(span => span.Kind == ActivityKind.Client);
        var values = spans.SelectMany(span => span.TagObjects.SelectMany(tag => new[] { tag.Key, tag.Value?.ToString() ?? string.Empty }))
            .Concat(spans.SelectMany(span => span.Events.SelectMany(e => e.Tags.Select(tag => tag.Value?.ToString() ?? string.Empty))))
            .Concat(spans.Select(span => span.DisplayName))
            .Concat(spans.Select(span => span.StatusDescription ?? string.Empty))
            .ToList();
        values.ShouldNotContain(value => value.Contains(WeatherKey, StringComparison.Ordinal) || value.Contains(NewsKey, StringComparison.Ordinal));
        values.ShouldNotContain(value => value.Contains(SearchText, StringComparison.Ordinal) || value.Contains(SearchCity, StringComparison.Ordinal));
        logs.Entries.ShouldNotBeEmpty();
        logs.Entries.ShouldNotContain(entry => entry.AllText.Contains(WeatherKey, StringComparison.Ordinal) || entry.AllText.Contains(NewsKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Logs_ShouldCarryTheRequestsTraceId_WhenASourceFailureIsLogged()
    {
        _upstream.StubKeylessSources();
        _upstream.StubStatus(UpstreamServer.GitHubPath, 500);
        var capture = new TelemetryCapture();
        var logs = new CapturingLoggerProvider();
        var (client, _) = _factory.CreateWithUpstreams(_upstream.SettingsWithoutCache(), capture.AddTo, logs);
        using var traced = capture.Traced(client);

        await AggregateAsync(traced, "?sources=repositories", HttpStatusCode.ServiceUnavailable);

        var warning = logs.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Category == "Aggregator.Infrastructure.Sources.SourceFetcher").ShouldHaveSingleItem();
        warning.Scopes.ShouldContainKey("TraceId");
        warning.Scopes["TraceId"].ShouldBe(capture.TraceId.ToHexString());
        warning.Scopes.ShouldContainKey("SpanId");
    }

    private Dictionary<string, string?> KeyedSettings()
    {
        var settings = _upstream.SettingsWithoutCache();
        settings["Sources:weather:ApiKey"] = WeatherKey;
        settings["Sources:news:ApiKey"] = NewsKey;
        return settings;
    }

    private static async Task AggregateAsync(HttpClient client, string query, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.GetAsync(ApiRoutes.Aggregation + query, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(expected);
    }
}
