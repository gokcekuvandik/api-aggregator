namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>Fails any outbound call that is not to this machine, so no test can reach a real API.</summary>
internal sealed class LoopbackOnlyHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        request.RequestUri is { IsLoopback: true }
            ? base.SendAsync(request, cancellationToken)
            : throw new InvalidOperationException($"Tests must not call external hosts, but a request went to {request.RequestUri?.Host}.");
}
