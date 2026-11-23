namespace Aggregator.Api.Http;

/// <summary>Structured log field names the API writes, so a query in the log store uses the same name the code does.</summary>
internal static class LogFields
{
    public const string Method = "Method";
    public const string Path = "Path";
    public const string EndpointName = "EndpointName";
    public const string StatusCode = "StatusCode";
    public const string InvalidFields = "InvalidFields";
}
