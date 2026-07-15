
namespace Beutl.Threading;

internal sealed class DispatcherOperation
{
    private readonly Action<Exception>? _abort;

    public DispatcherOperation(
        Action action,
        DispatchPriority priority,
        CancellationToken ct,
        Action<Exception>? abort = null)
    {
        Action = action;
        Priority = priority;
        Token = ct;
        _abort = abort;
        if (!ExecutionContext.IsFlowSuppressed())
        {
            ExecutionContext = ExecutionContext.Capture();
        }
    }

    public Action Action { get; }

    public DispatchPriority Priority { get; }

    public CancellationToken Token { get; }

    public ExecutionContext? ExecutionContext { get; }

    public void Run()
    {
        if (Token.IsCancellationRequested)
        {
            ExecutionContext?.Dispose();
            return;
        }

        if (ExecutionContext is { } ctx)
        {
            try
            {
                ExecutionContext.Run(ctx, _ => Action(), null);
            }
            finally
            {
                // Release the ExecutionContext even if Action() throws.
                ctx.Dispose();
            }
        }
        else
        {
            Action();
        }
    }

    public void Abort(Exception exception)
    {
        try
        {
            _abort?.Invoke(exception);
        }
        finally
        {
            ExecutionContext?.Dispose();
        }
    }
}
