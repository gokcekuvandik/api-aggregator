using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Aggregator.Api.Security;

internal static class SecurityServiceCollectionExtensions
{
    /// <summary>Tolerance for clock drift between the issuer and this host, tighter than the framework's five minutes.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>Registers JWT bearer validation from <c>Authentication:Schemes:Bearer</c>, which <c>dotnet user-jwts</c> fills in development.</summary>
    public static IServiceCollection AddApiSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Post-configured because binding replaces the validation parameters; claims keep their JWT names, so sub and scope are read as sent.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ClockSkew = ClockSkew;
        });
        services.AddSingleton<IValidateOptions<JwtBearerOptions>, JwtConfigurationValidator>();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();

        var authorization = services.AddAuthorizationBuilder();
        foreach (var scope in ApiScopes.All)
        {
            authorization.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireAssertion(context => ApiScopes.IsGranted(context.User, scope)));
        }

        services.AddCallerRateLimiting();

        return services;
    }
}
