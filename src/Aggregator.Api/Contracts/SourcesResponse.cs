using Aggregator.Application.Sources;

namespace Aggregator.Api.Contracts;

/// <summary>Body of <c>GET /api/v1/sources</c>.</summary>
public sealed record SourcesResponse(IReadOnlyList<SourceInfoResponse> Items);

public sealed record SourceInfoResponse(string Name, IReadOnlyList<string> Categories)
{
    public static SourceInfoResponse From(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.Name, source.Categories);
    }
}
