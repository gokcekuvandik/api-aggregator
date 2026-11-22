using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Aggregator.Api.Security;

/// <summary>Stops startup when the bearer scheme could not validate any token, instead of answering 401 to every request.</summary>
internal sealed class JwtConfigurationValidator : IValidateOptions<JwtBearerOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name != JwtBearerDefaults.AuthenticationScheme)
        {
            return ValidateOptionsResult.Skip;
        }

        var parameters = options.TokenValidationParameters;
        var hasAudience = HasAny([parameters.ValidAudience, .. parameters.ValidAudiences ?? []]);
        var hasAuthority = !string.IsNullOrWhiteSpace(options.Authority) || !string.IsNullOrWhiteSpace(options.MetadataAddress);
        var hasLocalKeys = HasAny([parameters.ValidIssuer, .. parameters.ValidIssuers ?? []])
            && (parameters.IssuerSigningKeys?.Any() ?? false);

        return hasAudience && (hasAuthority || hasLocalKeys) ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(OptionsValidationMessages.UnusableBearerScheme);
    }

    private static bool HasAny(IEnumerable<string?> values) => values.Any(value => !string.IsNullOrWhiteSpace(value));
}
