using System.ComponentModel.DataAnnotations;

namespace Aggregator.Infrastructure.Sources.Repositories.GitHub;

internal sealed class GitHubOptions : DataSourceOptions
{
    public GitHubOptions() => BaseAddress = new Uri("https://api.github.com/");

    [Required]
    public string Query { get; set; } = "topic:dotnet";

    [Range(1, 100)]
    public int MaxItems { get; set; } = 20;

    /// <summary>Optional; without it requests are anonymous and have a lower rate limit.</summary>
    [RegularExpression(HeaderSafePattern, ErrorMessage = OptionsValidationMessages.HeaderSafe)]
    public string? Token { get; set; }
}
