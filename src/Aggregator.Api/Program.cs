using Aggregator.Api;
using Aggregator.Api.Endpoints;
using Aggregator.Application;
using Aggregator.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services
    .AddApplication()
    .AddInfrastructure()
    .AddApi();

var app = builder.Build();

// Pipeline order (ADR-0003):
// 1. Exception handler first, so any later failure becomes a 500 ProblemDetails.
app.UseExceptionHandler();
// 2. Empty error responses (404 from routing, 400 from binding) get a ProblemDetails body.
app.UseStatusCodePages();
// 3. Authentication, authorization and rate limiting arrive with ADR-0010.

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

app.MapDefaultEndpoints();
app.MapApiEndpoints();

app.Run();
