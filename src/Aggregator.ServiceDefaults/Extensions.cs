using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthPathPrefix = "/health";
    private const string ReadinessEndpointPath = "/health/ready";
    private const string LivenessEndpointPath = "/health/live";

    /// <summary>The meter and activity source name of the Aggregator's own instruments; an architecture test keeps it equal to the application's constant.</summary>
    public const string AggregatorTelemetryName = "Aggregator";

    private const string PollyMeterName = "Polly";
    private const string ServiceNameVariable = "OTEL_SERVICE_NAME";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();

            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Traces, metrics and logs for the service, exported over OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set.
    /// The standard <c>OTEL_*</c> variables apply, such as <c>OTEL_SERVICE_NAME</c> and <c>OTEL_TRACES_SAMPLER</c>; the version is that of the assembly named after the application.
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var serviceVersion = ServiceVersion(builder.Environment.ApplicationName);
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: builder.Configuration[ServiceNameVariable] is { Length: > 0 } configured ? configured : builder.Environment.ApplicationName,
                serviceVersion: serviceVersion))
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(AggregatorTelemetryName, PollyMeterName);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(AggregatorTelemetryName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthPathPrefix)
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>The informational version MinVer stamps on the application assembly, or null when the assembly cannot be found.</summary>
    private static string? ServiceVersion(string applicationName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(applicationName)).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException)
        {
            return null;
        }
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(ReadinessEndpointPath)
            .WithHttpLogging(HttpLoggingFields.None);

        app.MapHealthChecks(LivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        })
            .WithHttpLogging(HttpLoggingFields.None);

        return app;
    }
}
