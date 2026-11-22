namespace Aggregator.ArchitectureTests;

/// <summary>Test names read as a specification: <c>Subject_ShouldOutcome</c>, with <c>_WhenCondition</c> unless the rule always holds.</summary>
public sealed class TestNamingConventionTests
{
    [Fact]
    public void TestMethods_ShouldFollowTheNamingConvention()
    {
        var tests = TestNameScanner.TestFiles()
            .SelectMany(path => TestNameScanner.FindTestMethods(File.ReadAllText(path), path))
            .ToList();

        TestNameScanner.TestProjects()
            .Where(project => !tests.Any(test => test.Path.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .ShouldBeEmpty("every test project should yield tests to the scan");
        tests.Where(test => !TestNameScanner.IsWellNamed(test.Name))
            .Select(test => $"{test.Location} {test.Name}")
            .ShouldBeEmpty();
    }

    [Fact]
    public void FindTestMethods_ShouldReportFactsAndTheoriesOnly_WhenSourceMixesMethods()
    {
        const string source = """
            class Sample
            {
                [Fact] public void A_ShouldB_WhenC() { }
                [Theory] public Task D_ShouldE() => Task.CompletedTask;
                [Xunit.FactAttribute(Skip = "x")] public void F_ShouldG() { }
                [SkippableTheory] public void H_ShouldI() { }
                [Obsolete] public void Helper() { }
            }
            """;

        TestNameScanner.FindTestMethods(source, "Sample.cs").Select(test => test.Name)
            .ShouldBe(["A_ShouldB_WhenC", "D_ShouldE", "F_ShouldG", "H_ShouldI"]);
    }

    [Theory]
    [InlineData("Apply_ShouldThrow_WhenPageIsLessThanOne", true)]
    [InlineData("Solution_ShouldNotBePackable", true)]
    [InlineData("Domain_ShouldNotDependOn_AnyOtherLayer", false)]
    [InlineData("ReturnsItems", false)]
    [InlineData("Apply_Throws_WhenPageIsZero", false)]
    [InlineData("Apply_ShouldThrow_whenPageIsZero", false)]
    public void IsWellNamed_ShouldMatchTheConvention_WhenGivenAName(string name, bool expected) =>
        TestNameScanner.IsWellNamed(name).ShouldBe(expected);
}
