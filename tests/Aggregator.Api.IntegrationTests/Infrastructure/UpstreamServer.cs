using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>One WireMock server standing in for every upstream API, with the recorded payloads; a test class shares it and resets it per test.</summary>
public sealed class UpstreamServer : IDisposable
{
    public const string OpenMeteoPath = "/v1/forecast";
    public const string OpenWeatherMapPath = "/data/2.5/forecast";
    public const string HackerNewsPath = "/api/v1/search";
    public const string NewsApiPath = "/v2/everything";
    public const string GitHubPath = "/search/repositories";

    /// <summary>Mappings at this priority win over the defaults that <see cref="StubKeylessSources"/> adds.</summary>
    public const int OverridePriority = 1;

    private const int DefaultPriority = 10;

    public WireMockServer Server { get; } = WireMockServer.Start();

    public string BaseAddress => $"{Server.Url}/";

    /// <summary>Points every source at this server and clears keys, so the keyless providers are chosen.</summary>
    public Dictionary<string, string?> Settings() => new()
    {
        ["Sources:weather:BaseAddress"] = BaseAddress,
        ["Sources:news:BaseAddress"] = BaseAddress,
        ["Sources:repositories:BaseAddress"] = BaseAddress,
        ["Sources:weather:ApiKey"] = string.Empty,
        ["Sources:news:ApiKey"] = string.Empty,
        ["Sources:repositories:Token"] = string.Empty,
    };

    public void StubKeylessSources()
    {
        StubJson(OpenMeteoPath, "open-meteo-forecast.json");
        StubJson(HackerNewsPath, "hn-search.json");
        StubJson(GitHubPath, "github-search.json");
    }

    public void StubJson(string path, string fixture, int priority = DefaultPriority, params (string Name, string Value)[] headers)
    {
        var response = JsonResponse(fixture);
        foreach (var (name, value) in headers)
        {
            response = response.WithHeader(name, value);
        }

        Server.Given(Request.Create().WithPath(path).UsingGet()).AtPriority(priority).RespondWith(response);
    }

    public static IResponseBuilder JsonResponse(string fixture) =>
        Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Fixture(fixture));

    public int RequestCount(string path, int? status = null) =>
        Server.LogEntries.Count(entry =>
            entry.RequestMessage?.Path == path && (status is null || Equals(entry.ResponseMessage?.StatusCode, status)));

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public void Dispose() => Server.Stop();
}
