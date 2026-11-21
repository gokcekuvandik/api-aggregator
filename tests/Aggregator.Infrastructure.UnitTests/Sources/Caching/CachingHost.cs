using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregator.Infrastructure.UnitTests.Sources.Caching;

/// <summary>A container with only the source cache services, and test items for it.</summary>
internal static class CachingHost
{
    public static readonly DateTimeOffset StoredAt = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSourceCaching();
        return services.BuildServiceProvider();
    }

    public static CachedItems Entry(string? provider = null) =>
        new([Item("r", new RepositoryDetails(Stars: 1, Forks: 0, Language: "C#"))], StoredAt, provider);

    public static AggregatedItem Item(string id, ItemDetails details) => new(
        Id: id,
        Source: "repositories",
        Category: "repositories",
        Title: $"Title of {id}",
        Summary: id == "n" ? null : $"Summary of {id}",
        Url: id == "w" ? null : new Uri($"https://example.test/{id}"),
        PublishedAt: StoredAt.AddHours(-1),
        Relevance: RelevanceScore.From(0.123456789),
        Details: details);
}
