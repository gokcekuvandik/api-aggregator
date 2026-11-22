using System.Runtime.InteropServices;
using static Aggregator.ArchitectureTests.ArchitectureFixture;
using Assembly = System.Reflection.Assembly;

namespace Aggregator.ArchitectureTests;

/// <summary>
/// The Domain project may reference only the shared framework.
/// Each reference must load from the runtime directory, because a "System." name alone is no proof.
/// </summary>
public sealed class DomainPurityTests
{
    private static readonly string RuntimeDirectory = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());

    [Fact]
    public void Domain_ShouldReferenceOnlySharedFrameworkAssemblies()
    {
        var nonFrameworkReferences = DomainAssembly.GetReferencedAssemblies()
            .Select(Assembly.Load)
            .Where(reference => !IsFromSharedFramework(reference))
            .Select(reference => $"{reference.GetName().Name} ({reference.Location})")
            .ToList();

        nonFrameworkReferences.ShouldBeEmpty();
    }

    private static bool IsFromSharedFramework(Assembly assembly) =>
        string.Equals(Path.GetDirectoryName(assembly.Location), RuntimeDirectory, StringComparison.OrdinalIgnoreCase);
}
