using Avalonia.Threading;

namespace Beutl.Services;

// Reports exceptions that escape UI-thread callbacks, such as async void handlers, instead of ending the process.
// Fatal ones stay unhandled for UnhandledExceptionHandler.
internal static class UiExceptionBoundary
{
    public static IDisposable Install(Dispatcher dispatcher)
    {
        dispatcher.UnhandledException += OnUnhandledException;
        return Disposable.Create(dispatcher, static d => d.UnhandledException -= OnUnhandledException);
    }

    private static void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (IsFatal(e.Exception))
            return;

        e.Handled = true;
        // Handle logs and notifies; it is async, so its own failure cannot escape here.
        _ = e.Exception.Handle();
    }

    private static bool IsFatal(Exception exception)
        => exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
