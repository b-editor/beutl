using Microsoft.Extensions.Logging;

namespace Beutl.FFmpegWorker;

internal sealed class StdoutLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StdoutLogger(categoryName);

    public void Dispose() { }

    private sealed class StdoutLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
                return;

            string message = formatter(state, exception);
            WorkerLog.Write(LevelText(logLevel), $"{category}: {message}", exception);
        }

        private static string LevelText(LogLevel level) =>
            level switch
            {
                LogLevel.Trace => "Trace",
                LogLevel.Debug => "Debug",
                LogLevel.Information => "Information",
                LogLevel.Warning => "Warning",
                LogLevel.Error => "Error",
                LogLevel.Critical => "Critical",
                _ => "Information",
            };
    }
}
