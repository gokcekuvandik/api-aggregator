using Aggregator.Application;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Assembly = System.Reflection.Assembly;

namespace Aggregator.ArchitectureTests;

/// <summary>Loads the production assemblies once for every architecture rule.</summary>
internal static class ArchitectureFixture
{
    public static readonly Assembly DomainAssembly = typeof(AggregatedItem).Assembly;
    public static readonly Assembly ApplicationAssembly = typeof(ApplicationServiceCollectionExtensions).Assembly;
    public static readonly Assembly InfrastructureAssembly = typeof(InfrastructureServiceCollectionExtensions).Assembly;
    public static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    public static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly).Build();
}
