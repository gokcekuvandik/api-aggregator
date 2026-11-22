using Aggregator.Api.Endpoints;
using ArchUnitNET.xUnitV3;
using static Aggregator.ArchitectureTests.ArchitectureFixture;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Aggregator.ArchitectureTests;

/// <summary>One sealed <see cref="IEndpoint"/> per feature, using only Application ports.</summary>
public sealed class EndpointConventionTests
{
    [Fact]
    public void Endpoints_ShouldBeSealed_WhenTheyImplementIEndpoint() =>
        Classes().That().ImplementInterface(typeof(IEndpoint))
            .Should().BeSealed()
            .Check(Architecture);

    [Fact]
    public void Endpoints_ShouldResideInEndpointsNamespace_WhenTheyImplementIEndpoint() =>
        Classes().That().ImplementInterface(typeof(IEndpoint))
            .Should().ResideInNamespaceMatching(EndpointsNamespacePattern)
            .Check(Architecture);

    [Fact]
    public void Endpoints_ShouldNotDependOnInfrastructure() =>
        Types().That().ResideInNamespaceMatching(EndpointsNamespacePattern)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(InfrastructureAssembly))
            .Check(Architecture);

    [Fact]
    public void EndpointScan_ShouldFindEveryFeature_WhenAssemblyIsLoaded() =>
        EndpointRegistration.DiscoverEndpoints()
            .Select(endpoint => endpoint.GetType().Name)
            .ShouldBe(["GetAggregationEndpoint", "GetAggregationStreamEndpoint", "GetSourcesEndpoint", "GetStatisticsEndpoint"]);
}
