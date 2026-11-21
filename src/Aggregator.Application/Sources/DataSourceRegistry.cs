using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Aggregator.Application.Sources;

/// <summary>Every registered source, resolved once and looked up by name ignoring case.</summary>
public sealed class DataSourceRegistry
{
    private readonly FrozenDictionary<string, IDataSource> _byName;

    public DataSourceRegistry(IEnumerable<IDataSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        All = [.. sources];
        var duplicate = All.GroupBy(source => source.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"More than one data source is named '{duplicate.Key}'.");
        }

        _byName = All.ToFrozenDictionary(source => source.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The sources in registration order.</summary>
    public IReadOnlyList<IDataSource> All { get; }

    public bool TryGet(string name, [NotNullWhen(true)] out IDataSource? source) => _byName.TryGetValue(name, out source);
}
