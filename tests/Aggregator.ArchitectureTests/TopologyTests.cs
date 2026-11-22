using System.Xml.Linq;
using static Aggregator.ArchitectureTests.ArchitectureFixture;

namespace Aggregator.ArchitectureTests;

/// <summary>One stateless application: no database, no message broker, and nothing packed as a NuGet library.</summary>
public sealed class TopologyTests
{
    private static readonly string[] StatefulClientPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
        "System.Data.SqlClient",
        "Npgsql",
        "MySqlConnector",
        "MongoDB.",
        "Dapper",
        "RabbitMQ.",
        "MassTransit",
        "Confluent.Kafka",
        "Azure.Messaging.",
        "NServiceBus",
    ];

    [Fact]
    public void ProductionAssemblies_ShouldReferenceNoDatabaseOrBrokerClient()
    {
        var references = ProductionAssemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies().Select(reference => (From: assembly.GetName().Name, To: reference.Name ?? "")))
            .ToList();

        references.ShouldContain(("Aggregator.Infrastructure", "Microsoft.Extensions.Caching.StackExchangeRedis"));
        references.Where(reference => StatefulClientPrefixes.Any(prefix => reference.To.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(reference => $"{reference.From} -> {reference.To}")
            .ShouldBeEmpty();
    }

    [Fact]
    public void Solution_ShouldNotBePackable()
    {
        var root = RepositoryPaths.Root();
        var projects = XDocument.Load(Path.Combine(root, RepositoryPaths.SolutionFile)).Descendants("Project")
            .Select(project => (string?)project.Attribute("Path") ?? throw new InvalidOperationException("A solution project has no Path."))
            .ToList();

        var overrides = projects
            .SelectMany(project => IsPackableValues(Path.Combine(root, project)).Where(value => value != "false").Select(value => $"{project}: {value}"))
            .ToList();

        IsPackableValues(Path.Combine(root, "Directory.Build.props")).ShouldBe(["false"]);
        projects.ShouldNotBeEmpty();
        overrides.ShouldBeEmpty();
    }

    private static List<string> IsPackableValues(string projectFile) =>
        XDocument.Load(projectFile).Descendants("IsPackable").Select(element => element.Value.Trim().ToLowerInvariant()).ToList();
}
