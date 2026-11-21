namespace Aggregator.Infrastructure.Sources;

/// <summary>Accepts only absolute http(s) links from upstream payloads, so clients never receive script or data URLs.</summary>
internal static class SafeUri
{
    public static Uri? Parse(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;
}
