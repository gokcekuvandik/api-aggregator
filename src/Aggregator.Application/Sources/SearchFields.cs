namespace Aggregator.Application.Sources;

/// <summary>The search fields of a query that a source sends upstream or scores with; the others are left out of its cache key.</summary>
[Flags]
public enum SearchFields
{
    None = 0,
    Text = 1,
    City = 2,
    From = 4,
    To = 8,
    All = Text | City | From | To,
}
