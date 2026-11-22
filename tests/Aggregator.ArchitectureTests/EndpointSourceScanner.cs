using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aggregator.ArchitectureTests;

/// <summary>
/// Reads endpoint source files and reports every <c>try</c> statement in them.
/// It works on syntax because compiled async methods always contain a compiler-generated try/catch.
/// </summary>
internal static class EndpointSourceScanner
{
    public static IReadOnlyList<string> EndpointFiles() =>
        [.. Directory.EnumerateFiles(Path.Combine(RepositoryPaths.Root(), "src", "Aggregator.Api", "Endpoints"), "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)];

    /// <summary>One <c>file:line</c> entry per <c>try</c> statement in the source.</summary>
    public static IReadOnlyList<string> FindTryStatements(string source, string path) =>
        [.. CSharpSyntaxTree.ParseText(source, path: path)
            .GetRoot()
            .DescendantNodes()
            .OfType<TryStatementSyntax>()
            .Select(statement => $"{path}:{statement.GetLocation().GetLineSpan().StartLinePosition.Line + 1}")];
}
