using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.xUnitV3;
using static Aggregator.ArchitectureTests.ArchitectureFixture;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Aggregator.ArchitectureTests;

/// <summary>Dependencies point inward (Api → Infrastructure → Application → Domain).</summary>
public sealed class LayerDependencyTests
{
    private static readonly GivenTypesConjunctionWithDescription DomainLayer = Layer(DomainAssembly, "Domain layer");
    private static readonly GivenTypesConjunctionWithDescription ApplicationLayer = Layer(ApplicationAssembly, "Application layer");
    private static readonly GivenTypesConjunctionWithDescription InfrastructureLayer = Layer(InfrastructureAssembly, "Infrastructure layer");
    private static readonly GivenTypesConjunctionWithDescription ApiLayer = Layer(ApiAssembly, "Api layer");

    [Fact]
    public void Domain_ShouldNotDependOnAnyOtherLayer() =>
        DomainLayer.Should()
            .NotDependOnAny(ApplicationLayer).AndShould()
            .NotDependOnAny(InfrastructureLayer).AndShould()
            .NotDependOnAny(ApiLayer)
            .Check(Architecture);

    [Fact]
    public void Application_ShouldNotDependOnInfrastructureOrApi() =>
        ApplicationLayer.Should()
            .NotDependOnAny(InfrastructureLayer).AndShould()
            .NotDependOnAny(ApiLayer)
            .Check(Architecture);

    [Fact]
    public void Infrastructure_ShouldNotDependOnApi() =>
        InfrastructureLayer.Should()
            .NotDependOnAny(ApiLayer)
            .Check(Architecture);

    private static GivenTypesConjunctionWithDescription Layer(Assembly assembly, string name) =>
        Types().That().ResideInAssembly(assembly).As(name);
}
