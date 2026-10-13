using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.TestInfrastructure;

// Records the level and exception of every log from the categories categoryFilter accepts.
public sealed class CapturingLoggerProvider(Func<string, bool> categoryFilter, bool throwOnLog = false) : ILoggerProvider
{
    private readonly bool _throwOnLog = throwOnLog;
    private bool _disposed;

    public ConcurrentQueue<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryFilter(categoryName));

    public void Dispose() => _disposed = true;

    private sealed class CapturingLogger(CapturingLoggerProvider provider, bool captures) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => !provider._disposed && captures;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            provider.Entries.Enqueue((logLevel, exception));
            if (provider._throwOnLog)
                throw new InvalidOperationException("Logging failed");
        }
    }
}
