namespace Aggregator.Infrastructure.Sources.News.HackerNews;

internal sealed class HackerNewsOptions : NewsOptions
{
    public HackerNewsOptions() => BaseAddress = new Uri("https://hn.algolia.com/");
}
