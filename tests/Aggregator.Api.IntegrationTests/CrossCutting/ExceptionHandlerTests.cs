using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Aggregator.Api.IntegrationTests.CrossCutting;

/// <summary>An unhandled exception is logged once with request context and answered with a safe 500.</summary>
public sealed class ExceptionHandlerTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{

    [Fact]
    public async Task UnhandledException_ShouldBeLoggedOnceWithRequestContext_WhenSourceThrows()
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs, new ThrowingDataSource());

        using var response = await client.GetAsync(ApiRoutes.Aggregation, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var error = logs.Entries.Where(entry => entry.Exception is not null).ShouldHaveSingleItem();
        error.Level.ShouldBe(LogLevel.Error);
        error.Exception.ShouldBeOfType<InvalidOperationException>();
        error.State["Method"].ShouldBe("GET");
        error.State["Path"].ShouldBe(ApiRoutes.Aggregation);
    }

    [Fact]
    public async Task UnhandledException_ShouldBeLoggedOnce_WhenClientDoesNotAcceptJson()
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs, new ThrowingDataSource());
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Aggregation);
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        logs.Entries.Where(entry => entry.Exception is not null).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AbortedRequest_ShouldNotBeLoggedAsError_WhenClientCancels()
    {
        var logs = new CapturingLoggerProvider();
        var slow = new DelayedDataSource(StubDataSource.InState("news", "Success"), TimeSpan.FromSeconds(30));
        using var client = factory.CreateClientWithLogs(logs, slow);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() => client.GetAsync(ApiRoutes.Aggregation, cancel.Token));

        var accessLog = await logs.WaitForEntryAsync(LogCategories.HttpLogging, TimeSpan.FromSeconds(10));
        accessLog.State["StatusCode"].ShouldBe("499");
        logs.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task UnhandledException_ShouldReturnGenericProblem_WhenSourceThrows()
    {
        using var client = factory.CreateProductionClientWith(new ThrowingDataSource());

        using var response = await client.GetAsync(ApiRoutes.Aggregation, TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.InternalServerError);
        problem.GetProperty("title").GetString().ShouldBe("An unexpected error occurred");
        problem.GetRawText().ShouldNotContain(ThrowingDataSource.SecretMessage);
        problem.TryGetProperty("detail", out _).ShouldBeFalse();
    }
}
