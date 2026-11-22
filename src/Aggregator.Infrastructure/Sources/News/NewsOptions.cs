using System.ComponentModel.DataAnnotations;

namespace Aggregator.Infrastructure.Sources.News;

/// <summary>What to search for and how many articles to take; shared by both news providers.</summary>
internal abstract class NewsOptions : DataSourceOptions
{
    [Required]
    public string Query { get; set; } = "dotnet";

    [Range(1, 100)]
    public int MaxItems { get; set; } = 20;
}
