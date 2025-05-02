namespace Beutl.Threading;

public class DispatcherUnhandledExceptionEventArgs(Exception exception) : EventArgs
{
    public bool Handled { get; set; }

    public Exception Exception { get; } = exception;
}
