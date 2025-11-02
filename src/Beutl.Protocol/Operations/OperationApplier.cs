namespace Beutl.Protocol.Operations;

public sealed class OperationApplier
{
    public void Apply(SyncOperation operation)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        operation.Apply(CreateDefaultContext());
    }

    public void Apply(SyncOperation operation, OperationExecutionContext context)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        operation.Apply(context);
    }

    private static OperationExecutionContext CreateDefaultContext()
    {
        return new OperationExecutionContext(BeutlApplication.Current);
    }
}
