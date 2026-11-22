using System.Net.Http.Headers;

namespace Aggregator.Infrastructure.Sources.Http;

/// <summary>The last successful body for a URL and the ETag that identifies it.</summary>
internal sealed record StoredResponse(EntityTagHeaderValue ETag, byte[] Body, string? MediaType);
