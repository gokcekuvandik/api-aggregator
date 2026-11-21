using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aggregator.Api.IntegrationTests.Snapshots;

/// <summary>
/// Compares normalised JSON with <c>{Class}.{Test}.verified.json</c> next to the test file.
/// On a mismatch it writes <c>.received.json</c> and fails; <c>AGGREGATOR_UPDATE_SNAPSHOTS=1</c> overwrites the verified file instead.
/// </summary>
internal static class JsonSnapshot
{
    private const string ScrubbedValue = "{scrubbed}";
    private const string UpdateVariable = "AGGREGATOR_UPDATE_SNAPSHOTS";

    /// <summary>Members whose values change on every run; the snapshot pins only their presence.</summary>
    private static readonly HashSet<string> VolatileMembers = new(StringComparer.Ordinal) { "traceId", "durationMs" };

    // Relaxed escaping keeps snapshots readable; the files are never embedded in HTML.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Task MatchAsync(
        string json,
        [CallerFilePath] string callerFile = "",
        [CallerMemberName] string testName = "")
    {
        var directory = Path.GetDirectoryName(callerFile)
            ?? throw new InvalidOperationException($"Cannot resolve the directory of '{callerFile}'.");
        var name = $"{Path.GetFileNameWithoutExtension(callerFile)}.{testName}";
        var update = Environment.GetEnvironmentVariable(UpdateVariable) == "1";
        return MatchAsync(json, directory, name, update);
    }

    internal static async Task MatchAsync(string json, string directory, string name, bool update)
    {
        var actual = Normalize(json);
        var verifiedPath = Path.Combine(directory, $"{name}.verified.json");
        var receivedPath = Path.Combine(directory, $"{name}.received.json");
        var cancellationToken = TestContext.Current.CancellationToken;

        if (update)
        {
            await File.WriteAllTextAsync(verifiedPath, actual, cancellationToken);
            File.Delete(receivedPath);
            return;
        }

        var verified = File.Exists(verifiedPath)
            ? NormalizeNewLines(await File.ReadAllTextAsync(verifiedPath, cancellationToken))
            : null;
        if (verified == actual)
        {
            File.Delete(receivedPath);
            return;
        }

        await File.WriteAllTextAsync(receivedPath, actual, cancellationToken);
        var problem = verified is null
            ? $"No snapshot for {name}."
            : $"Snapshot mismatch for {name}. {FirstDifference(verified, actual)}";
        Assert.Fail($"{problem}\nReview {receivedPath} and rename it to .verified.json to accept.");
    }

    /// <summary>Indented JSON with LF line endings and volatile member values replaced.</summary>
    internal static string Normalize(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new ArgumentException("The JSON is the literal null.", nameof(json));
        Scrub(node);
        return NormalizeNewLines(node.ToJsonString(Indented));
    }

    private static void Scrub(JsonNode node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array.OfType<JsonNode>())
            {
                Scrub(item);
            }

            return;
        }

        if (node is not JsonObject obj)
        {
            return;
        }

        foreach (var (key, child) in obj.ToList())
        {
            if (child is null)
            {
                continue;
            }

            if (VolatileMembers.Contains(key))
            {
                obj[key] = ScrubbedValue;
            }
            else
            {
                Scrub(child);
            }
        }
    }

    private static string NormalizeNewLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var line = 0;
        while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
        {
            line++;
        }

        var verified = line < expectedLines.Length ? expectedLines[line] : "<end of file>";
        var received = line < actualLines.Length ? actualLines[line] : "<end of file>";
        return $"First difference at line {line + 1}:\n  verified: {verified}\n  received: {received}";
    }
}
