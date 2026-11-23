using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aggregator.ArchitectureTests;

/// <summary>
/// Reads <c>[LoggerMessage]</c> templates and reports a placeholder written as a literal, such as <c>"{Path}"</c>.
/// A compliant template builds each placeholder from a constant, <c>"{" + LogFields.Path + "}"</c>; escaped braces are allowed.
/// It reads syntax only, so a template that is itself a named constant is not checked.
/// </summary>
internal static partial class LogTemplateScanner
{
    private static readonly string[] AttributeNames = ["LoggerMessage", "LoggerMessageAttribute"];

    /// <summary>One <c>file:line</c> entry per template that holds a literal placeholder.</summary>
    public static IReadOnlyList<string> FindLiteralPlaceholders(string source, string path) =>
        [.. Templates(source, path)
            .Where(template => template.DescendantNodesAndSelf()
                .OfType<LiteralExpressionSyntax>()
                .Any(literal => literal.IsKind(SyntaxKind.StringLiteralExpression) && LiteralPlaceholder().IsMatch(literal.Token.ValueText)))
            .Select(template => template.FileLine(path))];

    public static int CountTemplates(string source) => Templates(source, path: string.Empty).Count();

    private static IEnumerable<ExpressionSyntax> Templates(string source, string path) =>
        CSharpSyntaxTree.ParseText(source, path: path)
            .GetRoot()
            .DescendantNodes()
            .OfType<AttributeSyntax>()
            .Where(attribute => AttributeNames.Contains(attribute.Name.ToString(), StringComparer.Ordinal))
            .Select(MessageArgument)
            .OfType<ExpressionSyntax>();

    // The message is the named Message argument, or the last positional string argument.
    private static ExpressionSyntax? MessageArgument(AttributeSyntax attribute)
    {
        var arguments = attribute.ArgumentList?.Arguments ?? [];
        return arguments.FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == "Message")?.Expression
            ?? arguments.Where(argument => argument.NameEquals is null && argument.Expression is LiteralExpressionSyntax or BinaryExpressionSyntax)
                .Select(argument => argument.Expression)
                .LastOrDefault();
    }

    [GeneratedRegex(@"(?<!\{)\{(?!\{)[^{}]+\}(?!\})")]
    private static partial Regex LiteralPlaceholder();
}
