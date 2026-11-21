namespace Aggregator.Domain.Items;

/// <summary>How well an item matches the request, from 0 (not at all) to 1 (best). It is a heuristic, normalised per source.</summary>
public readonly record struct RelevanceScore : IComparable<RelevanceScore>
{
    public const double MinValue = 0;
    public const double MaxValue = 1;

    private RelevanceScore(double value) => Value = value;

    public static RelevanceScore None => default;

    public double Value { get; }

    /// <summary>Accepts only finite values from 0 to 1.</summary>
    public static RelevanceScore From(double value)
    {
        if (double.IsNaN(value) || value < MinValue || value > MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A relevance score must be between 0 and 1.");
        }

        return new RelevanceScore(value);
    }

    /// <summary>Forces any value into 0 to 1; NaN becomes 0.</summary>
    public static RelevanceScore Clamp(double value) =>
        double.IsNaN(value) ? None : new RelevanceScore(Math.Clamp(value, MinValue, MaxValue));

    public int CompareTo(RelevanceScore other) => Value.CompareTo(other.Value);

    public static bool operator <(RelevanceScore left, RelevanceScore right) => left.CompareTo(right) < 0;

    public static bool operator >(RelevanceScore left, RelevanceScore right) => left.CompareTo(right) > 0;

    public static bool operator <=(RelevanceScore left, RelevanceScore right) => left.CompareTo(right) <= 0;

    public static bool operator >=(RelevanceScore left, RelevanceScore right) => left.CompareTo(right) >= 0;
}
