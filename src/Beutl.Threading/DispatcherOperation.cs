
namespace Beutl.Threading;

internal sealed class DispatcherOperation
{
    public DispatcherOperation(Action action, DispatchPriority priority, CancellationToken ct)
    {
        Action = action;
        Priority = priority;
        Token = ct;
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
                // Action() が例外を投げても ExecutionContext を確実に解放する。
                ctx.Dispose();
            }
        }
        else
        {
            Action();
        }
    }
}
