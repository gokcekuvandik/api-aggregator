using Aggregator.Infrastructure.Sources;

namespace Aggregator.Infrastructure.UnitTests.Sources;

public sealed class SafeUriTests
{
    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://example.com/a?b=1")]
    public void Parse_ShouldReturnUri_WhenValueIsAbsoluteHttp(string value) =>
        SafeUri.Parse(value).ShouldBe(new Uri(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a uri")]
    public void Parse_ShouldReturnNull_WhenValueIsMissingOrUnsafe(string? value) =>
        SafeUri.Parse(value).ShouldBeNull();
}
