namespace Beutl.Threading;

// For tests: the arguments of Dispatcher.UnhandledException, which only tests subscribe to.
internal class DispatcherUnhandledExceptionEventArgs(Exception exception) : EventArgs
{
    public bool Handled { get; set; }

    public Exception Exception { get; } = exception;
}
