using System.Collections.Concurrent;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

// Captures the warnings RenderRequestOwner writes for masked failures, or throws from the logger to
// simulate a faulty provider. The global logger factory cannot drop a provider, so disposing only
// stops the capture.
internal sealed class RenderRequestOwnerLogCapture : ILoggerProvider
{
    private static readonly string s_category = typeof(RenderRequestOwner).FullName!;
    private readonly bool _throwOnLog;
    private volatile bool _stopped;

    private RenderRequestOwnerLogCapture(bool throwOnLog)
    {
        _throwOnLog = throwOnLog;
    }

    public ConcurrentQueue<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

    public static RenderRequestOwnerLogCapture Start(bool throwOnLog = false)
    {
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));

        var capture = new RenderRequestOwnerLogCapture(throwOnLog);
        Log.LoggerFactory.AddProvider(capture);
        return capture;
    }

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

    public void Dispose() => _stopped = true;

    private sealed class CaptureLogger(RenderRequestOwnerLogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => !owner._stopped && category == s_category;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            owner.Entries.Enqueue((logLevel, exception));
            if (owner._throwOnLog)
                throw new InvalidOperationException("logging-provider-failure");
        }
    }
}
