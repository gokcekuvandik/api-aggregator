using Aggregator.Application.Sources;
using ArchUnitNET.xUnitV3;
using static Aggregator.ArchitectureTests.ArchitectureFixture;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Aggregator.ArchitectureTests;

/// <summary>Adapters stay behind the port, and HTTP stays in Infrastructure.</summary>
public sealed class DataSourceConventionTests
{
    [Fact]
    public void Adapters_ShouldBeInternalAndSealed_WhenTheyImplementDataSource() =>
        Classes().That().ImplementInterface(typeof(IDataSource)).And().ResideInAssembly(InfrastructureAssembly)
            .Should().BeSealed().AndShould().NotBePublic()
            .Check(Architecture);

    [Fact]
    public void AdapterRule_ShouldSeeAdapters_WhenArchitectureIsLoaded() =>
        Architecture.Classes.ShouldContain(type => type.FullName == "Aggregator.Infrastructure.Sources.Repositories.GitHub.GitHubSource");

    [Fact]
    public void DomainAndApplication_ShouldNotDependOnHttpTypes() =>
        Types().That().ResideInAssembly(DomainAssembly).Or().ResideInAssembly(ApplicationAssembly)
            .Should().NotDependOnAny(Types().That().ResideInNamespace("System.Net.Http"))
            .Check(Architecture);

    [Fact]
    public void HttpRule_ShouldSeeHttpTypes_WhenArchitectureIsLoaded() =>
        Architecture.Types.ShouldContain(type => type.FullName == "System.Net.Http.HttpClient");
}
