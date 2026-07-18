using System.Reflection;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.UnitTests.Core;

[TestFixture]
[NonParallelizable]
public sealed class NotificationServiceTests
{
    [Test]
    public void Dispatch_WithoutHandler_InvokesShowFailed()
    {
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);

        NotificationService.Dispatch(notification, handler: null);

        Assert.That(failed, Is.EqualTo(1));
    }

    [Test]
    public void Dispatch_WhenHandlerThrows_InvokesShowFailedWithoutThrowing()
    {
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);
        var handler = new DelegateNotificationHandler(_ => throw new InvalidOperationException());

        Assert.That(() => NotificationService.Dispatch(notification, handler), Throws.Nothing);
        Assert.That(failed, Is.EqualTo(1));
    }

    [Test]
    public void Dispatch_WhenHandlerReportsFailureAndThrows_InvokesShowFailedOnce()
    {
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);
        var handler = new DelegateNotificationHandler(value =>
        {
            value.OnShowFailed!.Invoke();
            value.OnShowFailed.Invoke();
            throw new InvalidOperationException();
        });

        Assert.That(() => NotificationService.Dispatch(notification, handler), Throws.Nothing);
        Assert.That(failed, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Dispatch_WhenShowFailedThrows_DoesNotThrow(bool handlerThrows)
    {
        var notification = new Notification(
            "Title",
            "Message",
            OnShowFailed: () => throw new InvalidOperationException());
        INotificationServiceHandler? handler = handlerThrows
            ? new DelegateNotificationHandler(_ => throw new InvalidOperationException())
            : null;

        Assert.That(() => NotificationService.Dispatch(notification, handler), Throws.Nothing);
    }

    [Test]
    public void Dispatch_WhenHandlerSucceeds_DoesNotInvokeShowFailed()
    {
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);
        var handler = new DelegateNotificationHandler(_ => { });

        NotificationService.Dispatch(notification, handler);

        Assert.That(failed, Is.Zero);
    }

    [Test]
    public void Logger_UsesConfiguredFactoryAfterEarlyFallback()
    {
        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
        FieldInfo factoryField = typeof(Log).GetField("s_loggerFactory", Flags)!;
        FieldInfo loggerField = typeof(NotificationService).GetField("s_logger", Flags)!;
        PropertyInfo loggerProperty = typeof(NotificationService).GetProperty("Logger", Flags)!;
        object? originalFactory = factoryField.GetValue(null);
        object? originalLogger = loggerField.GetValue(null);
        var configuredLogger = new CaptureLogger();
        var configuredFactory = new CaptureLoggerFactory(configuredLogger);

        factoryField.SetValue(null, null);
        loggerField.SetValue(null, null);
        try
        {
            Assert.That(loggerProperty.GetValue(null), Is.SameAs(NullLogger.Instance));

            factoryField.SetValue(null, configuredFactory);

            Assert.That(loggerProperty.GetValue(null), Is.SameAs(configuredLogger));
            Assert.That(loggerField.GetValue(null), Is.SameAs(configuredLogger));
        }
        finally
        {
            loggerField.SetValue(null, originalLogger);
            factoryField.SetValue(null, originalFactory);
        }
    }

    private sealed class DelegateNotificationHandler(Action<Notification> show)
        : INotificationServiceHandler
    {
        public void Show(Notification notification)
        {
            show(notification);
        }
    }

    private sealed class CaptureLoggerFactory(ILogger logger) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return logger;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CaptureLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
