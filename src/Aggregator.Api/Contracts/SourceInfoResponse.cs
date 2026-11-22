using Aggregator.Application.Sources;

namespace Aggregator.Api.Contracts;

public sealed record SourceInfoResponse(string Name, IReadOnlyList<string> Categories)
{
    public static SourceInfoResponse From(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.Name, source.Categories);
    }
}
