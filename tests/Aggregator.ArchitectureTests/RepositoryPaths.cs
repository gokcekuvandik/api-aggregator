namespace Aggregator.ArchitectureTests;

/// <summary>Locates the repository by walking up from the test output to the solution file.</summary>
internal static class RepositoryPaths
{
    public const string SolutionFile = "Aggregator.slnx";

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
}
