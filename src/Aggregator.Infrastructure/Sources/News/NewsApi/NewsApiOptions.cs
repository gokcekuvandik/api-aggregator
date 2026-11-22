using System.ComponentModel.DataAnnotations;

namespace Aggregator.Infrastructure.Sources.News.NewsApi;

internal sealed class NewsApiOptions : NewsOptions
{
    public NewsApiOptions() => BaseAddress = new Uri("https://newsapi.org/");

    [Required]
    [RegularExpression(HeaderSafePattern, ErrorMessage = OptionsValidationMessages.HeaderSafe)]
    public string? ApiKey { get; set; }
}
