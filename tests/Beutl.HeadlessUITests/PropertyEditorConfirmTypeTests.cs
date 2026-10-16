using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls.PropertyEditors;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class PropertyEditorConfirmTypeTests
{
    [AvaloniaTest]
    public void Expect_returns_the_arguments_only_for_the_type_they_carry()
    {
        var editor = new StringEditor();
        var args = new PropertyEditorValueChangedEventArgs<string>("new", "old", PropertyEditor.ValueConfirmedEvent);

        Assert.Multiple(() =>
        {
            Assert.That(args.Expect<string>(editor), Is.SameAs(args));
            // Not raised, so no editor is its source and nothing is reported.
            Assert.That(args.Expect<int>(editor), Is.Null);
        });
    }

#if DEBUG
    [AvaloniaTest]
    public void A_mismatch_is_logged_and_fails_only_in_the_handler_on_the_raising_editor()
    {
        if (!Log.IsLoggerFactoryConfigured)
            Log.LoggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        using var logs = new LogRecorder();
        Log.LoggerFactory.AddProvider(logs);
        using var failures = new FailRecorder();
        var editor = new StringEditor();
        var parent = new StackPanel { Children = { editor } };
        editor.AddHandler(PropertyEditor.ValueConfirmedEvent, (s, e) => e.Expect<int>(s));
        parent.AddHandler(PropertyEditor.ValueConfirmedEvent, (s, e) => e.Expect<int>(s));

        editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<string>("new", "old", PropertyEditor.ValueConfirmedEvent));

        Assert.That(failures.Messages, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(failures.Messages[0], Does.Contain(nameof(StringEditor)).And.Contain(nameof(Int32)));
            Assert.That(logs.Messages, Is.EqualTo(failures.Messages));
        });
    }

    // Replaces the trace listeners, so Debug.Fail records its message instead of throwing or exiting.
    private sealed class FailRecorder : TraceListener
    {
        private readonly TraceListener[] _saved = [.. Trace.Listeners.Cast<TraceListener>()];

        public FailRecorder()
        {
            Trace.Listeners.Clear();
            Trace.Listeners.Add(this);
        }

        public List<string?> Messages { get; } = [];

        public override void Fail(string? message, string? detailMessage) => Messages.Add(message);

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
        }

        protected override void Dispose(bool disposing)
        {
            Trace.Listeners.Remove(this);
            Trace.Listeners.AddRange(_saved);
            base.Dispose(disposing);
        }
    }

    // Records the messages logged under PropertyEditorValueChangedEventArgs until disposed.
    private sealed class LogRecorder : ILoggerProvider, ILogger
    {
        private bool _disposed;

        public List<string?> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName)
            => categoryName == typeof(PropertyEditorValueChangedEventArgs).FullName ? this : NullLogger.Instance;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => !_disposed && logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Messages.Add(formatter(state, exception));
        }

        public void Dispose() => _disposed = true;
    }
#endif
}
