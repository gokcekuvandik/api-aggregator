using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>One captured log call, with its structured state rendered as text.</summary>
public sealed record CapturedLog(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, string?> State, Exception? Exception)
{
    /// <summary>Everything a log sink could write for this entry.</summary>
    public string AllText => string.Join('\n', [Message, .. State.Select(pair => $"{pair.Key}={pair.Value}"), Exception?.ToString() ?? string.Empty]);
}

/// <summary>Keeps every log entry in memory so tests can assert on what would reach a sink.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public IReadOnlyList<CapturedLog> InCategory(string category) =>
        [.. _entries.Where(entry => entry.Category == category)];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            var rendered = values
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().Value?.ToString(), StringComparer.Ordinal);
            entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), rendered, exception));
        }
    }
}
