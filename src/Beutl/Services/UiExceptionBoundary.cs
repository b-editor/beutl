using Avalonia.Threading;

namespace Beutl.Services;

// The one boundary for exceptions that escape a callback the UI thread runs, such as an async void event handler
// or an AsyncReactiveCommand subscription (the command awaits its subscriptions from async void). Avalonia raises
// Dispatcher.UnhandledException for them; unhandled, the exception ends the process with the user's unsaved edits.
// A fatal exception stays unhandled so that it still reaches UnhandledExceptionHandler.
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
        // Handle is the one log-and-notify policy. It is async, so a failure inside it cannot escape this handler.
        _ = e.Exception.Handle();
    }

    private static bool IsFatal(Exception exception)
        => exception is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
