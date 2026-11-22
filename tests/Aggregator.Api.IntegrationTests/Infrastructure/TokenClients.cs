using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

internal static class TokenClients
{
    /// <summary>A client of <paramref name="host"/> that sends <paramref name="token"/>, or no token when it is null.</summary>
    public static HttpClient CreateClientWithToken(this WebApplicationFactory<Program> host, string? token)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, token);
        return client;
    }
}
