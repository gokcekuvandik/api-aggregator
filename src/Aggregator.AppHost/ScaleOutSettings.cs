using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Aggregator.AppHost;

/// <summary>
/// The <c>ScaleOut</c> section: off by default, and when on, N replicas that share a Redis cache unless <c>Redis</c> is false.
/// A blank value counts as unset, so an empty environment variable keeps the default.
/// An invalid value fails with its key and value.
/// </summary>
internal sealed record ScaleOutSettings(bool Enabled, int Replicas, bool Redis)
{
    public const string SectionName = "ScaleOut";

    public const int DefaultReplicas = 3;

    public static ScaleOutSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new(
            Flag(section, nameof(Enabled), defaultValue: false),
            Count(section, nameof(Replicas), DefaultReplicas),
            Flag(section, nameof(Redis), defaultValue: true));
    }

    private static bool Flag(IConfigurationSection section, string key, bool defaultValue)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out var on)
            ? on
            : throw new InvalidOperationException($"{section.Path}:{key} must be true or false, but was '{value}'.");
    }

    private static int Count(IConfigurationSection section, string key, int defaultValue)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 1
            ? count
            : throw new InvalidOperationException($"{section.Path}:{key} must be a whole number of at least 1, but was '{value}'.");
    }
}
