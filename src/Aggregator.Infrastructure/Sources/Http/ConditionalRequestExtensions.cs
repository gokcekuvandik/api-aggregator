using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregator.Infrastructure.Sources.Http;

internal static class ConditionalRequestExtensions
{
    /// <summary>Adds ETag revalidation to one source's client.</summary>
    public static IHttpClientBuilder AddConditionalRequests(this IHttpClientBuilder builder)
    {
        builder.Services.TryAddSingleton<ConditionalResponseStore>();
        builder.Services.TryAddTransient<ConditionalRequestHandler>();
        return builder.AddHttpMessageHandler<ConditionalRequestHandler>();
    }
}
