using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aggregator.ArchitectureTests;

/// <summary>
/// Reads production source files and reports breaks of the code conventions that need syntax, not compiled types.
/// File layout is invisible after compilation, and message literals are folded into constants by then.
/// </summary>
internal static class ProductionSourceScanner
{
    private static readonly string SourceDirectory = Path.Combine(RepositoryPaths.Root(), "src");
    private static readonly string[] MessageFileSuffixes = ["Messages.cs", "Titles.cs"];
    private static readonly string[] MessageObjectTypes = ["ValidationResult", "SourceFailure"];
    private static readonly string[] MessageArgumentNames = ["title", "detail"];
    private static readonly string[] MessageInitializerNames = ["Title", "Detail"];
    private static readonly string[] FailureListMethods = ["Add", "AddRange"];
    private const string FailureListName = "failures";
    private const string ErrorDictionaryName = "errors";
    private const string OptionsResultType = "ValidateOptionsResult";
    private const string FailMethod = "Fail";
    private const string ValidateMethod = "Validate";

    private const string MessageObjectShape = "new ValidationResult or SourceFailure";
    private const string TargetTypedShape = "target-typed new in a method returning one";
    private const string OptionsFailShape = "ValidateOptionsResult.Fail";
    private const string OptionsValidateShape = "OptionsBuilder.Validate message";
    private const string FailureListShape = "failures.Add or AddRange";
    private const string ProblemArgumentShape = "title: or detail: argument";
    private const string ProblemInitializerShape = "Title or Detail initializer";
    private const string ErrorEntryShape = "errors[...] assignment";
    private const string YieldShape = "yield return in a string iterator";
    private const string WithMessageMethod = "WithMessage";
    private const string WithMessageShape = "FluentValidation WithMessage";

    public static IReadOnlyList<string> SourceFiles() => RepositoryPaths.SourceFiles(SourceDirectory, "*.cs");

    /// <summary>A description of the file's layout when it does not declare exactly one top-level type named after the file, otherwise null.</summary>
    public static string? FindLayoutViolation(string source, string path)
    {
        var root = CSharpSyntaxTree.ParseText(source, path: path).GetCompilationUnitRoot();
        var types = TopLevelTypeNames(root.Members).ToList();
        var names = types.Count == 0 ? "no type" : string.Join(", ", types);
        if (root.Members.OfType<GlobalStatementSyntax>().Any())
        {
            return types.Count == 0 ? null : $"{path}: top-level statements beside {names}";
        }

        return types is [var only] && only == Path.GetFileNameWithoutExtension(path) ? null : $"{path}: declares {names}";
    }

    /// <summary>Every message call shape the scanner knows; each must match real code, or its rule arm is dead.</summary>
    public static readonly IReadOnlyList<string> MessageShapes =
        [MessageObjectShape, TargetTypedShape, OptionsFailShape, OptionsValidateShape, FailureListShape, ProblemArgumentShape, ProblemInitializerShape, ErrorEntryShape, YieldShape, WithMessageShape];

    /// <summary>One <c>file:line</c> entry per user-facing message written as a literal at its call site.</summary>
    public static IReadOnlyList<string> FindInlineMessages(string source, string path) =>
        [.. MessageSites(source, path)
            .Select(site => site.Expression)
            .Where(ContainsStringLiteral)
            .Select(expression => expression.FileLine(path))
            .Distinct(StringComparer.Ordinal)];

    /// <summary>The message shapes the file holds, with or without a literal.</summary>
    public static IReadOnlySet<string> MatchedShapes(string source, string path) => MessageSites(source, path).Select(site => site.Shape).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(string Shape, ExpressionSyntax Expression)> MessageSites(string source, string path) =>
        MessageFileSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.Ordinal))
            ? []
            : CSharpSyntaxTree.ParseText(source, path: path).GetRoot().DescendantNodes()
                .Select(MessageSite)
                .Where(site => site.Expression is not null)
                .Select(site => (site.Shape, site.Expression!));

    private static IEnumerable<string> TopLevelTypeNames(SyntaxList<MemberDeclarationSyntax> members) =>
        members.SelectMany(member => member switch
        {
            BaseNamespaceDeclarationSyntax ns => TopLevelTypeNames(ns.Members),
            BaseTypeDeclarationSyntax type => [type.Identifier.Text],
            DelegateDeclarationSyntax @delegate => [@delegate.Identifier.Text],
            _ => [],
        });

    /// <summary>The shape and the expression that carries a message when the node is one of the known message call shapes.</summary>
    private static (string Shape, ExpressionSyntax? Expression) MessageSite(SyntaxNode node) => node switch
    {
        ObjectCreationExpressionSyntax creation when MessageObjectTypes.Contains(LastIdentifier(creation.Type)) => (MessageObjectShape, FirstArgument(creation.ArgumentList)),
        ImplicitObjectCreationExpressionSyntax creation when ReturnsMessageObject(creation) => (TargetTypedShape, FirstArgument(creation.ArgumentList)),
        InvocationExpressionSyntax invocation => InvocationMessage(invocation),
        ArgumentSyntax { NameColon: { } name } argument when MessageArgumentNames.Contains(name.Name.Identifier.Text) => (ProblemArgumentShape, argument.Expression),
        AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assignment when MessageInitializerNames.Contains(name.Identifier.Text) => (ProblemInitializerShape, assignment.Right),
        AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: ErrorDictionaryName } } } assignment => (ErrorEntryShape, assignment.Right),
        YieldStatementSyntax { Expression: { } expression } yield when YieldsStrings(yield) => (YieldShape, expression),
        _ => (string.Empty, null),
    };

    private static (string Shape, ExpressionSyntax? Expression) InvocationMessage(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: OptionsResultType }, Name.Identifier.Text: FailMethod } =>
            (OptionsFailShape, FirstArgument(invocation.ArgumentList)),
        MemberAccessExpressionSyntax { Name.Identifier.Text: ValidateMethod } when invocation.ArgumentList.Arguments.Count == 2 =>
            (OptionsValidateShape, invocation.ArgumentList.Arguments[1].Expression),
        MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: FailureListName }, Name: var method } when FailureListMethods.Contains(method.Identifier.Text) =>
            (FailureListShape, FirstArgument(invocation.ArgumentList)),
        MemberAccessExpressionSyntax { Name.Identifier.Text: WithMessageMethod } =>
            (WithMessageShape, FirstArgument(invocation.ArgumentList)),
        _ => (string.Empty, null),
    };

    private static bool ReturnsMessageObject(SyntaxNode node) =>
        EnclosingReturnType(node) is { } type && MessageObjectTypes.Contains(LastIdentifier(type));

    private static bool YieldsStrings(SyntaxNode node) =>
        EnclosingReturnType(node) is GenericNameSyntax { TypeArgumentList.Arguments: [PredefinedTypeSyntax { Keyword.Text: "string" }] };

    private static TypeSyntax? EnclosingReturnType(SyntaxNode node) => node.Ancestors().Select(ReturnType).OfType<TypeSyntax>().FirstOrDefault();

    private static TypeSyntax? ReturnType(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.ReturnType,
        LocalFunctionStatementSyntax function => function.ReturnType,
        _ => null,
    };

    private static ExpressionSyntax? FirstArgument(BaseArgumentListSyntax? arguments) => arguments?.Arguments.FirstOrDefault()?.Expression;

    private static string LastIdentifier(TypeSyntax type) => type.ToString().Split('.')[^1];

    private static bool ContainsStringLiteral(ExpressionSyntax expression) =>
        expression.DescendantNodesAndSelf().Any(node =>
            node is InterpolatedStringExpressionSyntax || node.IsKind(SyntaxKind.StringLiteralExpression));
}
