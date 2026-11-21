using Aggregator.Application;
using Aggregator.Domain;
using Aggregator.Infrastructure;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Assembly = System.Reflection.Assembly;

namespace Aggregator.ArchitectureTests;

/// <summary>Loads the production assemblies once for every architecture rule.</summary>
internal static class ArchitectureFixture
{
    public static readonly Assembly DomainAssembly = typeof(DomainAssemblyMarker).Assembly;
    public static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;
    public static readonly Assembly InfrastructureAssembly = typeof(InfrastructureAssemblyMarker).Assembly;
    public static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    public static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly).Build();
}
