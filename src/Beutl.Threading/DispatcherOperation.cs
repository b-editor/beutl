namespace Beutl.Threading;

internal sealed class DispatcherOperation
{
    public DispatcherOperation(Action action, DispatchPriority priority)
    {
        Action = action;
        Priority = priority;
        if (!ExecutionContext.IsFlowSuppressed())
        {
            ExecutionContext = ExecutionContext.Capture();
        }
    }

    public Action Action { get; }

    public DispatchPriority Priority { get; }

    public ExecutionContext? ExecutionContext { get; }

    public void Run()
    {
        if (ExecutionContext is { } ctx)
        {
            ExecutionContext.Run(ctx, _ => Action(), null);
        }
        else
        {
            Action();
        }
    }
}
