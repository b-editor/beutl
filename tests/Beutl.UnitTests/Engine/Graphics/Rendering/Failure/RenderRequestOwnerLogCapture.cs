using System.Collections.Concurrent;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

// Captures the warnings RenderRequestOwner writes for failures masked by the primary failure.
// The global logger factory cannot drop a provider, so disposing only stops the capture.
internal sealed class RenderRequestOwnerLogCapture : ILoggerProvider
{
    private static readonly string s_category = typeof(RenderRequestOwner).FullName!;
    private volatile bool _stopped;

    private RenderRequestOwnerLogCapture()
    {
    }

    public ConcurrentQueue<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

    public static RenderRequestOwnerLogCapture Start()
    {
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));

        var capture = new RenderRequestOwnerLogCapture();
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
            if (IsEnabled(logLevel))
                owner.Entries.Enqueue((logLevel, exception));
        }
    }
}
