using Aggregator.Application;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Microsoft.Extensions.Logging;
using Assembly = System.Reflection.Assembly;

namespace Aggregator.ArchitectureTests;

/// <summary>Loads the production assemblies once for every architecture rule.</summary>
internal static class ArchitectureFixture
{
    public static readonly Assembly DomainAssembly = typeof(AggregatedItem).Assembly;
    public static readonly Assembly ApplicationAssembly = typeof(ApplicationServiceCollectionExtensions).Assembly;
    public static readonly Assembly InfrastructureAssembly = typeof(InfrastructureServiceCollectionExtensions).Assembly;
    public static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    public static readonly Assembly[] ProductionAssemblies = [DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly];

    /// <summary>Loaded so rules against logging types match something instead of passing vacuously.</summary>
    public static readonly Assembly LoggingAbstractionsAssembly = typeof(ILogger).Assembly;

    /// <summary>Loaded so rules against HTTP types match something instead of passing vacuously.</summary>
    public static readonly Assembly HttpAssembly = typeof(HttpClient).Assembly;

    public const string EndpointsNamespacePattern = @"^Aggregator\.Api\.Endpoints(\..+)?$";

    public static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies([.. ProductionAssemblies, LoggingAbstractionsAssembly, HttpAssembly]).Build();
}
