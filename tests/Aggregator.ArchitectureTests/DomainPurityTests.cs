using System.Runtime.InteropServices;
using static Aggregator.ArchitectureTests.ArchitectureFixture;
using Assembly = System.Reflection.Assembly;

namespace Aggregator.ArchitectureTests;

/// <summary>
/// ADR-0001: the Domain project depends on the shared framework only. An assembly name is not
/// proof (NuGet packages such as System.Reactive start with "System."), so each reference is
/// resolved and must load from the runtime directory.
/// </summary>
public sealed class DomainPurityTests
{
    private static readonly string RuntimeDirectory = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());

    [Fact]
    public void Domain_ShouldReference_OnlySharedFrameworkAssemblies()
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
