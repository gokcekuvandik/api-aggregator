using Aggregator.Api;
using Aggregator.Api.Endpoints;
using Aggregator.Application;
using Aggregator.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi();

var app = builder.Build();

// HTTP logging wraps the exception handler so a failed request is logged with its final 500 status.
app.UseHttpLogging();

// The exception handler comes before everything else that can fail, so those failures become ProblemDetails.
app.UseExceptionHandler();
app.UseStatusCodePages();

// Authentication, authorization and rate limiting go here, in that order (ADR-0010).

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();
app.MapApiEndpoints();

app.Run();
