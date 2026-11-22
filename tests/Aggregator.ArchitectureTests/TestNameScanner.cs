using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aggregator.ArchitectureTests;

/// <summary>Finds xUnit test methods in source files and checks their names, so every test project is covered without referencing it.</summary>
internal static partial class TestNameScanner
{
    private static readonly string[] TestAttributeSuffixes = ["Fact", "Theory"];

    private static readonly string TestsDirectory = Path.Combine(RepositoryPaths.Root(), "tests");

    public sealed record TestMethod(string Name, string Path, int Line)
    {
        public string Location => $"{Path}:{Line}";
    }

    /// <summary>Every test project directory, found by its project file.</summary>
    public static IReadOnlyList<string> TestProjects() => [.. SourceFiles("*.csproj").Select(path => Path.GetDirectoryName(path)!)];

    public static IReadOnlyList<string> TestFiles() => SourceFiles("*.cs");

    private static IReadOnlyList<string> SourceFiles(string pattern) => RepositoryPaths.SourceFiles(TestsDirectory, pattern);

    public static IReadOnlyList<TestMethod> FindTestMethods(string source, string path) =>
        [.. CSharpSyntaxTree.ParseText(source, path: path)
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.AttributeLists.SelectMany(list => list.Attributes).Any(IsTestAttribute))
            .Select(method => new TestMethod(method.Identifier.Text, path, method.GetLocation().GetLineSpan().StartLinePosition.Line + 1))];

    public static bool IsWellNamed(string name) => ConventionPattern().IsMatch(name);

    /// <summary>Matches <c>[Fact]</c>, <c>[Xunit.Theory]</c>, <c>[FactAttribute]</c> and derived attributes such as <c>[SkippableFact]</c>.</summary>
    private static bool IsTestAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString().Split('.')[^1];
        name = name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
        return TestAttributeSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*_Should[A-Z][A-Za-z0-9]*(_When[A-Z][A-Za-z0-9]*)?$")]
    private static partial Regex ConventionPattern();
}
