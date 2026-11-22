using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Aggregator.Api.IntegrationTests.Observability;

/// <summary>One OpenTelemetry configuration in the service defaults, with the service's identity and the agreed meters and sources.</summary>
public sealed class TelemetrySetupTests : IClassFixture<AggregatorApiFactory>, IClassFixture<UpstreamServer>
{
    private const string Repositories = "?sources=repositories&q=dotnet";

    private readonly AggregatorApiFactory _factory;
    private readonly UpstreamServer _upstream;

    public TelemetrySetupTests(AggregatorApiFactory factory, UpstreamServer upstream)
    {
        _factory = factory;
        _upstream = upstream;
        _upstream.Server.Reset();
        _upstream.StubKeylessSources();
    }

    [Fact]
    public void Resource_ShouldNameTheServiceAndCarryItsBuildVersion_WhenNoServiceNameIsConfigured()
    {
        var (_, services) = _factory.CreateWithUpstreams(_upstream.Settings());

        var attributes = ResourceAttributes(services);

        attributes["service.name"].ShouldBe("Aggregator.Api");
        attributes["service.version"].ShouldBe(ApiVersion);
        ApiVersion.ShouldNotStartWith("1.0.0", customMessage: "MinVer should version the API from git, not the SDK default.");
    }

    [Fact]
    public void Resource_ShouldUseTheConfiguredServiceName_WhenOtelServiceNameIsSet()
    {
        var settings = _upstream.Settings();
        settings["OTEL_SERVICE_NAME"] = "aggregator-under-test";
        var (_, services) = _factory.CreateWithUpstreams(settings);

        ResourceAttributes(services)["service.name"].ShouldBe("aggregator-under-test");
    }

    [Fact]
    public async Task Metrics_ShouldExportTheAggregatorKestrelAndPollyMeters_WhenASourceIsCalled()
    {
        var capture = new TelemetryCapture();
        var (client, services) = _factory.CreateWithUpstreams(_upstream.SettingsWithoutCache(), capture.AddTo);
        using var _ = client;

        await AggregateAsync(client);

        // The API runs on TestServer, so a probe on a meter named like Kestrel's shows the subscription without crediting the WireMock server's Kestrel.
        var probe = $"probe.kestrel.{Guid.NewGuid():N}";
        using var kestrel = new Meter("Microsoft.AspNetCore.Server.Kestrel");
        kestrel.CreateCounter<long>(probe).Add(1);
        var names = capture.MetricNames(services);
        names.ShouldContain("aggregator.source.requests");
        names.ShouldContain(probe);
        names.ShouldContain(name => name.StartsWith("resilience.polly.", StringComparison.Ordinal), customMessage: string.Join(", ", names));
    }

    [Fact]
    public async Task Traces_ShouldRecordTheServerAndClientSpans_WhenSamplingIsLeftAtItsDefault()
    {
        var capture = new TelemetryCapture();
        var (client, _) = _factory.CreateWithUpstreams(_upstream.SettingsWithoutCache(), capture.AddTo);
        using var traced = capture.Traced(client);

        await AggregateAsync(traced);

        capture.Spans.ShouldContain(span => span.Source.Name == "Microsoft.AspNetCore");
        capture.Spans.ShouldContain(span => span.Source.Name == "System.Net.Http");
    }

    [Fact]
    public void Sampler_ShouldFollowTheParent_WhenNoSamplerIsConfigured()
    {
        var (_, services) = _factory.CreateWithUpstreams(_upstream.Settings());

        SamplerOf(services).ShouldBeOfType<ParentBasedSampler>();
    }

    [Fact]
    public void Sampler_ShouldComeFromTheStandardVariable_WhenOtelTracesSamplerIsSet()
    {
        var settings = _upstream.Settings();
        settings["OTEL_TRACES_SAMPLER"] = "always_off";
        var (_, services) = _factory.CreateWithUpstreams(settings);

        SamplerOf(services).ShouldBeOfType<AlwaysOffSampler>();
    }

    private static string ApiVersion =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    private static Dictionary<string, object> ResourceAttributes(IServiceProvider services) =>
        services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>
    /// Span listeners are process-wide, so a span another test host samples reaches this host's exporter too; the sampler this host built is read instead.
    /// The property is internal to the SDK; the sampler types are public.
    /// </summary>
    private static Sampler SamplerOf(IServiceProvider services)
    {
        var provider = services.GetRequiredService<TracerProvider>();
        var property = provider.GetType().GetProperty("Sampler", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("The OpenTelemetry SDK no longer exposes its sampler; update this test.");
        return (Sampler)property.GetValue(provider)!;
    }

    private static async Task AggregateAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ApiRoutes.Aggregation + Repositories, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
