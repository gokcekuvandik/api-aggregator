using Aggregator.Api;
using Aggregator.Api.Endpoints;
using Aggregator.Api.Security;
using Aggregator.Application;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi()
    .AddApiSecurity();
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(SourceCacheMetrics.MeterName));

var app = builder.Build();

// HTTP logging wraps the exception handler so a failed request is logged with its final 500 status.
app.UseHttpLogging();

// The exception handler comes before everything else that can fail, so those failures become ProblemDetails.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// After authorization, so the limiter partitions by the validated subject and a rejected token never takes a token.
app.UseRateLimiter();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference(options => options.AddPreferredSecuritySchemes([BearerSecurityTransformer.SchemeName]));
}

app.MapDefaultEndpoints();
app.MapApiEndpoints();

app.Run();
