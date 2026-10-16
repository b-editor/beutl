using System.Collections.Concurrent;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Editor.Components.ColorScopesTab.Views.Scopes;
using Beutl.Logging;
using Beutl.Media.Source;
using Beutl.Testing.Headless;
using Microsoft.Extensions.Logging;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public class ScopeControlRenderFailureTests
{
    [AvaloniaTest]
    public async Task A_failed_scope_render_is_logged()
    {
        // Log keeps the first factory it is given, and the headless tests set none otherwise.
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        using var logs = new CapturingProvider();
        Log.LoggerFactory.AddProvider(logs);
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(8, 8));
        var scope = new FailingScope { Width = 120, Height = 80, SourceBitmap = source };
        using var host = new ControlHost(scope);

        scope.Refresh();
        await HeadlessTestHelpers.WaitUntilAsync(() => !logs.Exceptions.IsEmpty);

        Assert.That(logs.Exceptions, Is.All.TypeOf<InvalidOperationException>());
    }

    private sealed class FailingScope : ScopeControlBase
    {
        protected override string[]? VerticalAxisLabels => null;

        protected override string[]? HorizontalAxisLabels => null;

        protected override WriteableBitmap? RenderScope(
            BtlBitmap sourceBitmap, int targetWidth, int targetHeight, WriteableBitmap? existingBitmap)
            => throw new InvalidOperationException("The scope could not be rendered.");
    }

    // Records the exceptions the scope base logs at Warning or above, until disposed.
    private sealed class CapturingProvider : ILoggerProvider, ILogger
    {
        private volatile bool _disposed;

        public ConcurrentQueue<Exception> Exceptions { get; } = new();

        public ILogger CreateLogger(string categoryName)
            => categoryName == typeof(ScopeControlBase).FullName ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => !_disposed && logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && exception != null)
                Exceptions.Enqueue(exception);
        }

        public void Dispose() => _disposed = true;
    }
}
