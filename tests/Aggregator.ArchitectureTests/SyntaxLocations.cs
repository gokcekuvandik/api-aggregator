using Microsoft.CodeAnalysis;

namespace Aggregator.ArchitectureTests;

/// <summary>Formats a syntax node's position as <c>path:line</c>, the shape every scanner reports.</summary>
internal static class SyntaxLocations
{
    public static string FileLine(this SyntaxNode node, string path) =>
        $"{path}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
}
