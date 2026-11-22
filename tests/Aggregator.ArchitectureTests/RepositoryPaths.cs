namespace Aggregator.ArchitectureTests;

/// <summary>Locates the repository by walking up from the test output to the solution file.</summary>
internal static class RepositoryPaths
{
    public const string SolutionFile = "Aggregator.slnx";

    private static readonly string[] BuildOutputDirectories = ["bin", "obj"];

    public static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No {SolutionFile} above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>Files under <paramref name="directory"/> that match <paramref name="pattern"/>, without build output, in ordinal order.</summary>
    public static IReadOnlyList<string> SourceFiles(string directory, string pattern) =>
        [.. Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(directory, path))
            .Order(StringComparer.Ordinal)];

    // Only segments below the scanned directory count, so a checkout under a folder named bin or obj is still scanned.
    private static bool IsBuildOutput(string directory, string path) =>
        Path.GetRelativePath(directory, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => BuildOutputDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
}
