namespace Aggregator.Api.Contracts;

/// <summary>Messages a caller reads in the 400 body when a request fails validation, or that a parse of an unchecked request throws.</summary>
internal static class ValidationMessages
{
    public const string FromAfterTo = "The field from must not be later than to.";
    public const string UnknownSortField = $"The field sortBy must be one of: {SortParameters.FieldNames}.";
    public const string UnknownSortDirection = $"The field sortOrder must be one of: {SortParameters.DirectionNames}.";
    public const string InvalidSortField = $"sortBy must be one of: {SortParameters.FieldNames}.";
    public const string InvalidSortDirection = $"sortOrder must be one of: {SortParameters.DirectionNames}.";

    // FluentValidation fills the placeholders with the field's wire name and the rule's limits.
    public const string TooLong = "The field {PropertyName} must be a string with a maximum length of {MaxLength}.";
    public const string OutOfRange = "The field {PropertyName} must be between {From} and {To}.";
}
