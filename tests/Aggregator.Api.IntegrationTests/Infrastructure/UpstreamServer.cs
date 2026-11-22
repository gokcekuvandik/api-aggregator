using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>One WireMock server standing in for every upstream API, with the recorded payloads; a test class shares it and resets it per test.</summary>
public sealed class UpstreamServer : IAsyncLifetime
{
    public const string OpenMeteoPath = "/v1/forecast";
    public const string OpenWeatherMapPath = "/data/2.5/forecast";
    public const string HackerNewsPath = "/api/v1/search";
    public const string NewsApiPath = "/v2/everything";
    public const string GitHubPath = "/search/repositories";
    public const string GeocodingPath = "/v1/search";

    /// <summary>Each source with the path of the provider that answers it by default.</summary>
    public static readonly (string Source, string Path)[] Upstreams =
    [
        ("weather", OpenMeteoPath),
        ("news", HackerNewsPath),
        ("repositories", GitHubPath),
    ];

    /// <summary>Mappings at this priority win over the defaults that <see cref="StubKeylessSources"/> adds.</summary>
    public const int OverridePriority = 1;

    private const int DefaultPriority = 10;

    public WireMockServer Server { get; } = WireMockServer.Start();

    public string BaseAddress => $"{Server.Url}/";

    /// <summary>Points every source at this server and clears keys, so the keyless providers are chosen.</summary>
    public Dictionary<string, string?> Settings() => new()
    {
        ["Sources:weather:BaseAddress"] = BaseAddress,
        ["Sources:weather:GeocodingAddress"] = BaseAddress,
        ["Sources:weather:Secondary:BaseAddress"] = BaseAddress,
        ["Sources:weather:Secondary:GeocodingAddress"] = BaseAddress,
        ["Sources:news:Secondary:BaseAddress"] = BaseAddress,
        ["Sources:news:BaseAddress"] = BaseAddress,
        ["Sources:repositories:BaseAddress"] = BaseAddress,
        ["Sources:weather:ApiKey"] = string.Empty,
        ["Sources:news:ApiKey"] = string.Empty,
        ["Sources:repositories:Token"] = string.Empty,
    };

    /// <summary>Like <see cref="Settings"/>, but with the source cache off, for tests of what happens below it on every call.</summary>
    public Dictionary<string, string?> SettingsWithoutCache()
    {
        var settings = Settings();
        foreach (var (source, _) in Upstreams)
        {
            settings[$"Sources:{source}:Cache:Enabled"] = "false";
        }

        return settings;
    }

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

    /// <summary>Answers every GET on <paramref name="path"/> with <paramref name="status"/>, ahead of the default stubs.</summary>
    public void StubStatus(string path, int status, int priority = OverridePriority) =>
        Server.Given(Request.Create().WithPath(path).UsingGet()).AtPriority(priority).RespondWith(Response.Create().WithStatusCode(status));

    public static IResponseBuilder JsonResponse(string fixture) =>
        Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(Fixture(fixture));

    public int RequestCount(string path, int? status = null) =>
        Server.LogEntries.Count(entry =>
            entry.RequestMessage?.Path == path && (status is null || Equals(entry.ResponseMessage?.StatusCode, status)));

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>The first requests to a fresh server can stall for seconds under a busy test run, so one is spent here rather than in a test.</summary>
    public async ValueTask InitializeAsync()
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(new Uri($"{Server.Url}/warm-up"), TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Server.Stop();
        return ValueTask.CompletedTask;
    }
}
