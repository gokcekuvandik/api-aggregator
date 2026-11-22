using Microsoft.Extensions.Configuration;

namespace Aggregator.AppHost.UnitTests;

public sealed class ScaleOutSettingsTests
{
    [Fact]
    public void Read_ShouldReturnTheDefaults_WhenTheSectionIsMissing()
    {
        var settings = ScaleOutSettings.Read(Configuration([]));

        settings.ShouldBe(new ScaleOutSettings(Enabled: false, Replicas: ScaleOutSettings.DefaultReplicas, Redis: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_ShouldReturnTheDefaults_WhenEveryValueIsBlank(string blank)
    {
        var settings = ScaleOutSettings.Read(Configuration(new()
        {
            ["ScaleOut:Enabled"] = blank,
            ["ScaleOut:Replicas"] = blank,
            ["ScaleOut:Redis"] = blank,
        }));

        settings.ShouldBe(new ScaleOutSettings(Enabled: false, Replicas: ScaleOutSettings.DefaultReplicas, Redis: true));
    }

    [Theory]
    [InlineData("true", "1", "false", true, 1, false)]
    [InlineData("TRUE", "5", "True", true, 5, true)]
    [InlineData(" false ", " 2 ", "FALSE", false, 2, false)]
    [InlineData("true", "2147483647", "true", true, int.MaxValue, true)]
    public void Read_ShouldParseTheValues_WhenTheyAreValid(string enabled, string replicas, string redis, bool expectedEnabled, int expectedReplicas, bool expectedRedis)
    {
        var settings = ScaleOutSettings.Read(Configuration(new()
        {
            ["ScaleOut:Enabled"] = enabled,
            ["ScaleOut:Replicas"] = replicas,
            ["ScaleOut:Redis"] = redis,
        }));

        settings.ShouldBe(new ScaleOutSettings(expectedEnabled, expectedReplicas, expectedRedis));
    }

    [Theory]
    [InlineData("Enabled", "yes")]
    [InlineData("Enabled", "1")]
    [InlineData("Redis", "on")]
    public void Read_ShouldThrowNamingTheKeyAndValue_WhenAFlagIsNotABoolean(string key, string value)
    {
        var exception = Should.Throw<InvalidOperationException>(() => ScaleOutSettings.Read(Configuration(new() { [$"ScaleOut:{key}"] = value })));

        exception.Message.ShouldBe($"ScaleOut:{key} must be true or false, but was '{value}'.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("two")]
    [InlineData("2.5")]
    [InlineData("2147483648")]
    public void Read_ShouldThrowNamingTheKeyAndValue_WhenReplicasIsNotAWholeNumberOfAtLeastOne(string value)
    {
        var exception = Should.Throw<InvalidOperationException>(() => ScaleOutSettings.Read(Configuration(new() { ["ScaleOut:Replicas"] = value })));

        exception.Message.ShouldBe($"ScaleOut:Replicas must be a whole number of at least 1, but was '{value}'.");
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
