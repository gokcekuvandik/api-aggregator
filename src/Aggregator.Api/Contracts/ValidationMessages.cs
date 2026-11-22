namespace Aggregator.Api.Contracts;

/// <summary>Messages a caller reads in the 400 body when a request fails validation, or that a parse of an unchecked request throws.</summary>
internal static class ValidationMessages
{
    public const string FromAfterTo = "The field from must not be later than to.";
    public const string UnknownSortField = $"The field sortBy must be one of: {SortParameters.FieldNames}.";
    public const string UnknownSortDirection = $"The field sortOrder must be one of: {SortParameters.DirectionNames}.";
    public const string InvalidSortField = $"sortBy must be one of: {SortParameters.FieldNames}.";
    public const string InvalidSortDirection = $"sortOrder must be one of: {SortParameters.DirectionNames}.";
}
