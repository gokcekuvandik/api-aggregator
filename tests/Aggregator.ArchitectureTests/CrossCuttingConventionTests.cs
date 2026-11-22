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
