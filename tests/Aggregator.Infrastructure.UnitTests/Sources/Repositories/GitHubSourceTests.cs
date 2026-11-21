using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Relevance;
using Aggregator.Infrastructure.Sources.Repositories.GitHub;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources.Repositories;

public sealed class GitHubSourceTests : IDisposable
{
    private readonly SourceTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task FetchAsync_ShouldSearchByStars_WhenQueryIsConfigured()
    {
        _host.Http.Expect($"{StubHttpClientFactory.DefaultBaseAddress}search/repositories")
            .WithExactQueryString("q=topic%3Adotnet&sort=stars&order=desc&per_page=20")
            .WithHeaders("Accept", "application/vnd.github+json")
            .WithHeaders("X-GitHub-Api-Version", "2022-11-28")
            .Respond("application/json", SourceTestHost.Fixture("github-search.json"));

        await Source(new GitHubOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldSendBearerToken_WhenTokenIsConfigured()
    {
        _host.Http.Expect("*").WithHeaders("Authorization", "Bearer ghp_test").Respond("application/json", """{"items":[]}""");

        await Source(new GitHubOptions { Token = "ghp_test" }).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldStayAnonymous_WhenNoTokenIsConfigured()
    {
        var authorized = false;
        _host.Http.When("*")
            .With(request => { authorized = request.Headers.Authorization is not null; return true; })
            .Respond("application/json", """{"items":[]}""");

        await Source(new GitHubOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        authorized.ShouldBeFalse();
    }

    [Fact]
    public async Task FetchAsync_ShouldMapRepositories_WhenSearchReturnsItems()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("github-search.json"));

        var result = await Source(new GitHubOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items.Select(i => i.Id).ShouldBe(["repositories:17620347", "repositories:2"]);
        items[0].Title.ShouldBe("dotnet/aspnetcore");
        items[0].Category.ShouldBe("repositories");
        items[0].Url.ShouldBe(new Uri("https://github.com/dotnet/aspnetcore"));
        items[0].PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));
        items[1].Summary.ShouldBeNull();
    }

    [Fact]
    public async Task FetchAsync_ShouldUseBestMatch_WhenRequestHasText()
    {
        _host.Http.Expect("*").WithExactQueryString("q=aspnet%20core&per_page=20").Respond("application/json", """{"items":[]}""");

        await Source(new GitHubOptions()).FetchAsync(SourceTestHost.Query with { Text = "aspnet core" }, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldMapDetailsAndBlendedRelevance_WhenSearchReturnsItems()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("github-search.json"));

        var result = await Source(new GitHubOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items[0].Details.ShouldBe(new RepositoryDetails(Stars: 36000, Forks: 0, Language: "C#"));
        items[1].Details.ShouldBe(new RepositoryDetails(Stars: 1, Forks: 0, Language: null));
        items[0].Relevance.Value.ShouldBe(1.0);
        items[1].Relevance.Value.ShouldBe(0.5 + (0.5 * Math.Log(2) / Math.Log(36001)), tolerance: 1e-9);
    }

    [Fact]
    public void Source_ShouldDescribeItself_WhenListed()
    {
        var source = Source(new GitHubOptions());

        source.Name.ShouldBe("repositories");
        source.Categories.ShouldBe(["repositories"]);
        new GitHubOptions().BaseAddress.ShouldBe(new Uri("https://api.github.com/"));
    }

    private GitHubSource Source(GitHubOptions options) => new(_host.Fetcher, Options.Create(options), new RepositoryRelevanceScorer());
}
