using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.Endpoints;

/// <summary>The search, filter and sort parameters of the aggregation endpoint (ADR-0005).</summary>
public sealed class AggregationQueryTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    private const string Url = ApiRoutes.Aggregation;
    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ShouldSortByRelevanceDescending_WhenNoSortIsGiven()
    {
        using var client = factory.CreateClientWith(News(), Weather());

        var ids = await IdsAsync(client, Url);

        ids.ShouldBe(["n-high", "w-mid", "n-low", "w-low"]);
    }

    public static TheoryData<string, string[]> Sorts => new()
    {
        { "sortBy=date&sortOrder=asc", ["n-low", "w-low", "w-mid", "n-high"] },
        { "sortBy=DATE&sortOrder=Desc", ["n-high", "w-mid", "w-low", "n-low"] },
        { "sortBy=relevance&sortOrder=asc", ["w-low", "n-low", "w-mid", "n-high"] },
        { "sortBy=source&sortOrder=desc", ["w-mid", "w-low", "n-high", "n-low"] },
        { "sortBy=Source&sortOrder=Desc", ["w-mid", "w-low", "n-high", "n-low"] },
    };

    [Theory]
    [MemberData(nameof(Sorts))]
    public async Task Get_ShouldSortItems_WhenSortIsGiven(string query, string[] expected)
    {
        using var client = factory.CreateClientWith(News(), Weather());

        var ids = await IdsAsync(client, $"{Url}?{query}");

        ids.ShouldBe(expected);
    }

    [Fact]
    public async Task Get_ShouldReturnOnlyCategoryAndSkipOtherSources_WhenCategoryIsGiven()
    {
        var news = News();
        using var client = factory.CreateClientWith(news, Weather());

        var body = await GetJsonAsync(client, $"{Url}?category=Weather");

        Ids(body).ShouldBe(["w-mid", "w-low"]);
        body.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ShouldBe(["weather"]);
        news.LastQuery.ShouldBeNull();
    }

    [Fact]
    public async Task Get_ShouldQueryOnlyNamedSources_WhenSourcesListIsGiven()
    {
        var weather = Weather();
        using var client = factory.CreateClientWith(News(), weather, Repositories());

        var body = await GetJsonAsync(client, $"{Url}?sources=NEWS,%20repositories,,news");

        body.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ShouldBe(["news", "repositories"]);
        weather.LastQuery.ShouldBeNull();
    }

    [Fact]
    public async Task Get_ShouldKeepBothBounds_WhenDateRangeIsGiven()
    {
        using var client = factory.CreateClientWith(News(), Weather());

        var ids = await IdsAsync(client, $"{Url}?from=2026-11-21T10:00:00Z&to=2026-11-21T14:00:00%2B02:00");

        ids.ShouldBe(["w-mid", "w-low"]);
    }

    [Fact]
    public async Task Get_ShouldPassSearchFieldsToSources_WhenTheyAreGiven()
    {
        var news = News();
        using var client = factory.CreateClientWith(news);

        await GetJsonAsync(client, $"{Url}?q=%20rust%20lang%20&city=Paris&from=2026-11-20T00:00:00Z&to=2026-11-21T00:00:00Z&page=2&pageSize=5");

        var query = news.LastQuery.ShouldNotBeNull();
        query.Text.ShouldBe("rust lang");
        query.City.ShouldBe("Paris");
        query.From.ShouldBe(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        query.To.ShouldBe(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero));
        query.Page.ShouldBe(2);
        query.PageSize.ShouldBe(5);
    }

    [Fact]
    public async Task Get_ShouldTreatBlankTextFieldsAsMissing_WhenTheyAreEmpty()
    {
        var news = News();
        using var client = factory.CreateClientWith(news);

        await GetJsonAsync(client, $"{Url}?q=&city=%20&category=&sources=");

        var query = news.LastQuery.ShouldNotBeNull();
        query.Text.ShouldBeNull();
        query.City.ShouldBeNull();
        query.Category.ShouldBeNull();
        query.Sources.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_ShouldDescribeDetailsWithDiscriminator_WhenItemsAreReturned()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("weather", "Success"), StubDataSource.InState("repositories", "Success"));

        var body = await GetJsonAsync(client, $"{Url}?sortBy=source&sortOrder=asc");

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items[0].GetProperty("details").GetProperty("$type").GetString().ShouldBe("repository");
        items[0].GetProperty("details").GetProperty("stars").GetInt32().ShouldBe(42);
        items[1].GetProperty("details").GetProperty("$type").GetString().ShouldBe("weather");
        items[1].GetProperty("details").GetProperty("location").GetString().ShouldBe("Athens");
        items[1].GetProperty("relevance").GetDouble().ShouldBe(0.5);
    }

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "from=2026-11-21T00:00:00Z&to=2026-11-20T23:59:59Z", "from" },
        { "sources=news,nope", "sources" },
        { "category=sports", "category" },
        { "sources=weather&category=news", "category" },
        { "sortBy=7", "sortBy" },
        { "sortOrder=-1", "sortOrder" },
        { $"q={new string('a', 201)}", "q" },
        { $"city={new string('a', 101)}", "city" },
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task Get_ShouldReturnValidationProblem_WhenQueryIsInvalid(string query, string errorKey)
    {
        using var client = factory.CreateClientWith(News(), Weather());

        using var response = await client.GetAsync($"{Url}?{query}", TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldContain(errorKey);
    }

    [Theory]
    [InlineData("sortBy=popularity")]
    [InlineData("sortOrder=up")]
    [InlineData("from=yesterday")]
    public async Task Get_ShouldReturn400Problem_WhenValueCannotBeParsed(string query)
    {
        using var client = factory.CreateProductionClientWith(News());

        using var response = await client.GetAsync($"{Url}?{query}", TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_ShouldReadDatesAsUtc_WhenNoOffsetIsGiven()
    {
        var news = News();
        using var client = factory.CreateClientWith(news, Weather());

        var ids = await IdsAsync(client, $"{Url}?from=2026-11-21T12:00:00&to=2026-11-21T12:00:00");

        ids.ShouldBe(["w-mid"]);
        var query = news.LastQuery.ShouldNotBeNull();
        query.From.ShouldBe(Noon);
        query.From!.Value.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task Get_ShouldAcceptRange_WhenFromEqualsTo()
    {
        using var client = factory.CreateClientWith(News(), Weather());

        var ids = await IdsAsync(client, $"{Url}?from=2026-11-21T12:00:00Z&to=2026-11-21T12:00:00Z");

        ids.ShouldBe(["w-mid"]);
    }

    private static RecordingDataSource News() => new(
        "news",
        RecordingDataSource.Item("news", "n-high", Noon.AddHours(1), 0.9),
        RecordingDataSource.Item("news", "n-low", Noon.AddHours(-3), 0.3));

    private static RecordingDataSource Weather() => new(
        "weather",
        RecordingDataSource.Item("weather", "w-mid", Noon, 0.6),
        RecordingDataSource.Item("weather", "w-low", Noon.AddHours(-1), 0.2));

    private static RecordingDataSource Repositories() => new("repositories");

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        return await response.ShouldBeJson(HttpStatusCode.OK);
    }

    private static async Task<string?[]> IdsAsync(HttpClient client, string url) => Ids(await GetJsonAsync(client, url));

    private static string?[] Ids(JsonElement body) =>
        [.. body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString())];
}
