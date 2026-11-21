using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources;

/// <summary>Checks the base address and every data annotation on a source's options.</summary>
internal sealed class DataSourceOptionsValidator<TOptions>(string section) : IValidateOptions<TOptions>
    where TOptions : DataSourceOptions
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [.. AddressFailures(options.BaseAddress)];
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
        {
            failures.AddRange(results.Select(result => $"{section}: {result.ErrorMessage}"));
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private IEnumerable<string> AddressFailures(Uri? address)
    {
        var key = $"{section}:{nameof(DataSourceOptions.BaseAddress)}";
        if (address is null || address.OriginalString.Length == 0)
        {
            yield return $"{key} is required.";
            yield break;
        }

        if (!address.IsAbsoluteUri)
        {
            yield return $"{key} must be an absolute URI.";
            yield break;
        }

        var secure = address.Scheme == Uri.UriSchemeHttps || (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback);
        if (!secure)
        {
            yield return $"{key} must use https; plain http is allowed only for loopback hosts.";
        }

        if (address.UserInfo.Length > 0)
        {
            yield return $"{key} must not contain credentials; use the source's key or token setting.";
        }

        if (!address.AbsolutePath.EndsWith('/'))
        {
            yield return $"{key} must end with '/' so relative request paths keep its path.";
        }
    }
}
