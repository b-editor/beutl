using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Services;

// Reports exceptions that escape UI-thread callbacks, such as async void handlers, instead of ending the process.
// Fatal ones stay unhandled for UnhandledExceptionHandler.
internal static class UiExceptionBoundary
{
    public static IDisposable Install(Dispatcher dispatcher, INotificationService notifications)
    {
        void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e) => HandleUnhandledException(e, notifications);
        dispatcher.UnhandledException += OnUnhandledException;
        return Disposable.Create(() => dispatcher.UnhandledException -= OnUnhandledException);
    }

    private static void HandleUnhandledException(DispatcherUnhandledExceptionEventArgs e, INotificationService notifications)
    {
        if (IsFatal(e.Exception))
            return;

        e.Handled = true;
        // Handle logs and notifies; it is async, so its own failure cannot escape here.
        _ = e.Exception.Handle(notifications);
    }

    private static bool IsFatal(Exception exception)
        => exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
