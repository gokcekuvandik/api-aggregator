using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>One captured log call, with its structured state and the values of its scopes, such as the trace and span ids, rendered as text.</summary>
public sealed record CapturedLog(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, string?> State,
    Exception? Exception,
    IReadOnlyDictionary<string, string?> Scopes)
{
    /// <summary>Everything a log sink could write for this entry.</summary>
    public string AllText => string.Join('\n', [
        Message,
        .. State.Select(pair => $"{pair.Key}={pair.Value}"),
        .. Scopes.Select(pair => $"{pair.Key}={pair.Value}"),
        Exception?.ToString() ?? string.Empty]);
}

/// <summary>Keeps every log entry in memory so tests can assert on what would reach a sink.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public IReadOnlyList<CapturedLog> InCategory(string category) =>
        [.. _entries.Where(entry => entry.Category == category)];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries, () => _scopes);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private static Dictionary<string, string?> Render(object? state)
    {
        var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
        return values
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value?.ToString(), StringComparer.Ordinal);
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries, Func<IExternalScopeProvider> scopes) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => scopes().Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopeValues = new Dictionary<string, string?>(StringComparer.Ordinal);
            scopes().ForEachScope(
                (scope, values) =>
                {
                    foreach (var (key, value) in Render(scope))
                    {
                        values[key] = value;
                    }
                },
                scopeValues);
            entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), Render(state), exception, scopeValues));
        }
    }
}
