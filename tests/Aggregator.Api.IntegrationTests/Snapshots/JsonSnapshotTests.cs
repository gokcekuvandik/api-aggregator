using Xunit.Sdk;

namespace Aggregator.Api.IntegrationTests.Snapshots;

/// <summary>The snapshot helper must fail when it should; otherwise every snapshot test is vacuous.</summary>
public sealed class JsonSnapshotTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("aggregator-snapshots-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Normalize_ShouldScrubVolatileMembers_WhenNestedInObjectsAndArrays()
    {
        var normalized = JsonSnapshot.Normalize("""{"traceId":"00-abc","sources":[{"name":"news","durationMs":12.5}]}""");

        normalized.ShouldNotContain("00-abc");
        normalized.ShouldNotContain("12.5");
        normalized.ShouldContain("\"name\": \"news\"");
    }

    [Fact]
    public void Normalize_ShouldKeepNull_WhenVolatileMemberIsNull() =>
        JsonSnapshot.Normalize("""{"durationMs":null}""").ShouldContain("\"durationMs\": null");

    [Fact]
    public void Normalize_ShouldIgnoreLineEndingsAndFormatting_WhenJsonIsEquivalent() =>
        JsonSnapshot.Normalize("{\r\n\"a\" : 1}").ShouldBe(JsonSnapshot.Normalize("""{"a":1}"""));

    [Fact]
    public async Task MatchAsync_ShouldFailAndWriteReceived_WhenNoSnapshotExists()
    {
        await Should.ThrowAsync<FailException>(() => JsonSnapshot.MatchAsync("""{"a":1}""", _directory, "case", update: false));

        File.Exists(Path.Combine(_directory, "case.received.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task MatchAsync_ShouldFailWithDifference_WhenSnapshotDiffers()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "case.verified.json"), JsonSnapshot.Normalize("""{"a":1}"""), TestContext.Current.CancellationToken);

        var failure = await Should.ThrowAsync<FailException>(() => JsonSnapshot.MatchAsync("""{"a":2}""", _directory, "case", update: false));

        failure.Message.ShouldContain("\"a\": 2");
        File.Exists(Path.Combine(_directory, "case.received.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task MatchAsync_ShouldPassAndRemoveStaleReceived_WhenSnapshotMatchesWithCrlf()
    {
        var verified = JsonSnapshot.Normalize("""{"a":1}""").Replace("\n", "\r\n", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(_directory, "case.verified.json"), verified, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, "case.received.json"), "stale", TestContext.Current.CancellationToken);

        await JsonSnapshot.MatchAsync("""{"a":1}""", _directory, "case", update: false);

        File.Exists(Path.Combine(_directory, "case.received.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task MatchAsync_ShouldWriteVerified_WhenUpdateIsRequested()
    {
        await JsonSnapshot.MatchAsync("""{"a":1}""", _directory, "case", update: true);

        (await File.ReadAllTextAsync(Path.Combine(_directory, "case.verified.json"), TestContext.Current.CancellationToken))
            .ShouldBe(JsonSnapshot.Normalize("""{"a":1}"""));
    }
}
