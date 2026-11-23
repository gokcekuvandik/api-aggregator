namespace Aggregator.ArchitectureTests;

/// <summary>Source-level conventions: one type per file named after it, and user-facing messages kept in message classes.</summary>
public sealed class CodeConventionTests
{
    [Fact]
    public void SourceFiles_ShouldDeclareOneTopLevelTypeNamedAfterTheFile()
    {
        var files = ProductionSourceScanner.SourceFiles();

        files.ShouldNotBeEmpty();
        files.Select(path => ProductionSourceScanner.FindLayoutViolation(File.ReadAllText(path), path))
            .OfType<string>()
            .ShouldBeEmpty();
    }

    [Fact]
    public void SourceFiles_ShouldNotWriteUserFacingMessagesInline()
    {
        var sources = ProductionSourceScanner.SourceFiles().Select(path => (Path: path, Text: File.ReadAllText(path))).ToList();

        sources.SelectMany(source => ProductionSourceScanner.MatchedShapes(source.Text, source.Path)).Distinct()
            .ShouldBe(ProductionSourceScanner.MessageShapes, ignoreOrder: true);
        sources.SelectMany(source => ProductionSourceScanner.FindInlineMessages(source.Text, source.Path)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("namespace N; public sealed record Sample(int A);", null)]
    [InlineData("namespace N { internal static class Sample { private sealed class Nested; } }", null)]
    [InlineData("var x = 1; System.Console.WriteLine(x);", null)]
    [InlineData("namespace N; public sealed record Sample; public enum Kind { A }", "Sample.cs: declares Sample, Kind")]
    [InlineData("namespace N; public sealed record Other;", "Sample.cs: declares Other")]
    [InlineData("namespace N; public delegate void Sample();", null)]
    [InlineData("namespace N;", "Sample.cs: declares no type")]
    [InlineData("var x = 1; class Helper;", "Sample.cs: top-level statements beside Helper")]
    public void FindLayoutViolation_ShouldReportTheFile_WhenItBreaksOneTypePerFile(string source, string? expected) =>
        ProductionSourceScanner.FindLayoutViolation(source, "Sample.cs").ShouldBe(expected);

    [Fact]
    public void FindInlineMessages_ShouldReportEveryKnownMessageShape_WhenItHoldsALiteral()
    {
        const string source = """
            class Sample
            {
                IEnumerable<ValidationResult> A() { yield return new ValidationResult("bad", ["x"]); }
                object B() => Results.Problem(title: "t", detail: $"d {1}");
                object C() => new ProblemDetails { Title = "t" };
                void D(Dictionary<string, string[]> errors) { errors["x"] = [$"unknown {1}"]; }
                object E() => ValidateOptionsResult.Fail("f" + "g");
                object F(OptionsBuilder<object> b) => b.Validate(o => true, $"{1} must");
                void G(List<string> failures) { failures.Add("f"); failures.AddRange(new[] { "x" }.Select(x => $"{x}")); }
                IEnumerable<string> H() { yield return $"{1} is required."; }
                static SourceFailure I(string name) => new($"{name} timed out.");
                object J() => new SourceFailure("down");
                static ValidationResult K() => new("bad");
                void L() { static ValidationResult Local() => new("bad"); }
                IEnumerable<string> M(string key) { yield return Describe(key, "is required"); }
                void N(Rules rules) { rules.RuleFor(x => x.Page).GreaterThan(0).WithMessage("too small"); }
            }
            """;

        ProductionSourceScanner.FindInlineMessages(source, "Sample.cs").ShouldBe(
            ["Sample.cs:3", "Sample.cs:4", "Sample.cs:5", "Sample.cs:6", "Sample.cs:7", "Sample.cs:8", "Sample.cs:9", "Sample.cs:10", "Sample.cs:11", "Sample.cs:12", "Sample.cs:13", "Sample.cs:14", "Sample.cs:15", "Sample.cs:16"]);
        ProductionSourceScanner.MatchedShapes(source, "Sample.cs").ShouldBe(ProductionSourceScanner.MessageShapes, ignoreOrder: true);
    }

    [Fact]
    public void FindInlineMessages_ShouldAllowConstantsAndInvariants_WhenNoMessageShapeHoldsALiteral()
    {
        const string source = """
            class Sample
            {
                IEnumerable<ValidationResult> A() { yield return new ValidationResult(Messages.Bad, ["x"]); }
                object B() => Results.Problem(title: ProblemTitles.Unexpected, statusCode: 500);
                object C() => throw new UnreachableException($"Unknown {1}.");
                object D(OptionsBuilder<object> b) => b.Validate(o => o.Name == "x", Messages.Invalid(section));
                string E() => "plain data";
                object F(Circuit circuit) => circuit.Fail("broken");
                IEnumerable<SseItem> G() { yield return new SseItem("done"); }
                void H(Rules rules) { rules.RuleFor(x => x.Page).GreaterThan(0).WithMessage(ValidationMessages.OutOfRange); }
            }
            """;

        ProductionSourceScanner.FindInlineMessages(source, "Sample.cs").ShouldBeEmpty();
    }

    [Fact]
    public void FindInlineMessages_ShouldSkipMessageClasses_WhenTheFileHoldsThem()
    {
        const string source = """class ValidationMessages { static object A() => new ValidationResult("bad"); }""";

        ProductionSourceScanner.FindInlineMessages(source, "ValidationMessages.cs").ShouldBeEmpty();
    }
}
