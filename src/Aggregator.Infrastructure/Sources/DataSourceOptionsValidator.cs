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

        List<string> failures = [.. options.Addresses.SelectMany(entry => AddressFailures(entry.Key, entry.Address))];
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
        {
            failures.AddRange(results.Select(result => OptionsValidationMessages.InSection(section, result.ErrorMessage)));
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private IEnumerable<string> AddressFailures(string setting, Uri? address)
    {
        var key = $"{section}:{setting}";
        if (address is null || address.OriginalString.Length == 0)
        {
            yield return OptionsValidationMessages.Required(key);
            yield break;
        }

        if (!address.IsAbsoluteUri)
        {
            yield return OptionsValidationMessages.NotAbsolute(key);
            yield break;
        }

        var secure = address.Scheme == Uri.UriSchemeHttps || (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback);
        if (!secure)
        {
            yield return OptionsValidationMessages.NotSecure(key);
        }

        if (address.UserInfo.Length > 0)
        {
            yield return OptionsValidationMessages.HasCredentials(key);
        }

        if (!address.AbsolutePath.EndsWith('/'))
        {
            yield return OptionsValidationMessages.NoTrailingSlash(key);
        }
    }
}
