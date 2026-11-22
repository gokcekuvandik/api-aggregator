namespace Aggregator.Application.Aggregation;

/// <summary>Messages a caller reads when a query names sources or categories the registry does not know.</summary>
internal static class ValidationMessages
{
    public static string UnknownSource(string name, string known) => $"Unknown source '{name}'. Known sources: {known}.";

    public static string UnknownCategory(string category, string known) => $"Unknown category '{category}'. Known categories: {known}.";

    public static string CategoryNotServed(string category) => $"None of the selected sources serves category '{category}'.";
}
