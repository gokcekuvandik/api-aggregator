using ArchUnitNET.xUnitV3;
using static Aggregator.ArchitectureTests.ArchitectureFixture;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Aggregator.ArchitectureTests;

/// <summary>Endpoints hold business logic only; logging and exception handling live in the pipeline.</summary>
public sealed class CrossCuttingConventionTests
{
    [Fact]
    public void Endpoints_ShouldNotDependOnLogging() =>
        Types().That().ResideInNamespaceMatching(EndpointsNamespacePattern)
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Microsoft.Extensions.Logging"))
            .Check(Architecture);

    [Fact]
    public void LoggingRule_ShouldSeeLoggingTypes_WhenArchitectureIsLoaded() =>
        Architecture.Types.ShouldContain(type => type.FullName == "Microsoft.Extensions.Logging.ILogger");

    [Fact]
    public async Task Endpoints_ShouldNotContainTryStatements_WhenSourceIsScanned()
    {
        var files = EndpointSourceScanner.EndpointFiles();
        var violations = new List<string>();

        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            violations.AddRange(EndpointSourceScanner.FindTryStatements(source, file));
        }

        files.ShouldNotBeEmpty();
        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Endpoints_ShouldNotValidateRequests_WhenSourceIsScanned()
    {
        var files = EndpointSourceScanner.EndpointFiles();
        var violations = new List<string>();

        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            violations.AddRange(EndpointSourceScanner.FindValidationCode(source, file));
        }

        files.ShouldNotBeEmpty();
        violations.ShouldBeEmpty();
    }

    [Fact]
    public void ValidationScan_ShouldReportLines_WhenEndpointValidatesItself()
    {
        const string source = """
            class Sample
            {
                object Run(Service service, Query query)
                {
                    var errors = service.Validate(query);
                    return TypedResults.ValidationProblem(errors);
                }
            }
            """;

        EndpointSourceScanner.FindValidationCode(source, "Sample.cs").ShouldBe(["Sample.cs:5", "Sample.cs:6"]);
    }

    [Fact]
    public void ApiTypes_ShouldNotImplementIValidatableObject_WhenValidatorsOwnTheRules() =>
        ApiAssembly.GetTypes().Where(type => type.IsAssignableTo(typeof(System.ComponentModel.DataAnnotations.IValidatableObject)))
            .Select(type => type.FullName)
            .ShouldBeEmpty();

    [Fact]
    public async Task ApiLogTemplates_ShouldTakeFieldNamesFromLogFields_WhenSourceIsScanned()
    {
        var files = RepositoryPaths.SourceFiles(Path.Combine(RepositoryPaths.Root(), "src", "Aggregator.Api"), "*.cs");
        var templates = 0;
        var violations = new List<string>();

        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            templates += LogTemplateScanner.CountTemplates(source);
            violations.AddRange(LogTemplateScanner.FindLiteralPlaceholders(source, file));
        }

        templates.ShouldBeGreaterThan(0);
        violations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""[LoggerMessage(Level = LogLevel.Error, Message = "Failed {Path}")]""")]
    [InlineData("""[LoggerMessage(LogLevel.Error, "Failed {Path}")]""")]
    [InlineData("""[LoggerMessageAttribute(Message = "Failed " + "{Path}")]""")]
    public void LogTemplateScan_ShouldReportLine_WhenPlaceholderIsLiteral(string attribute)
    {
        var source = $$"""
            partial class Sample
            {
                {{attribute}}
                static partial void Log(ILogger logger, string path);
            }
            """;

        LogTemplateScanner.FindLiteralPlaceholders(source, "Sample.cs").ShouldBe(["Sample.cs:3"]);
    }

    [Fact]
    public void LogTemplateScan_ShouldReportNothing_WhenPlaceholdersComeFromConstants()
    {
        const string source = """
            partial class Sample
            {
                [LoggerMessage(Level = LogLevel.Error, Message = "Failed {" + LogFields.Path + "} after {{retries}}")]
                static partial void Log(ILogger logger, string path);
            }
            """;

        LogTemplateScanner.FindLiteralPlaceholders(source, "Sample.cs").ShouldBeEmpty();
        LogTemplateScanner.CountTemplates(source).ShouldBe(1);
    }

    [Fact]
    public void TryScan_ShouldReportLine_WhenSourceHasTryCatch()
    {
        const string source = """
            class Sample
            {
                void Run()
                {
                    try { } catch { }
                }
            }
            """;

        EndpointSourceScanner.FindTryStatements(source, "Sample.cs").ShouldBe(["Sample.cs:5"]);
    }

    [Fact]
    public void TryScan_ShouldReportNothing_WhenSourceHasNoTry() =>
        EndpointSourceScanner.FindTryStatements("class Sample { void Run() { } }", "Sample.cs").ShouldBeEmpty();
}
