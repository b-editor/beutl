using Beutl.Editor.Infrastructure;
using Beutl.Editor.Operations;

namespace Beutl.Editor;

public sealed class OperationApplier
{
    public OperationApplier(CoreObject root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public CoreObject Root { get; }

    public void Apply(ChangeOperation operation)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        // Suppress publishing while applying remote operations to prevent echo-back
        using (PublishingSuppression.Enter())
        {
            operation.Apply(CreateDefaultContext());
        }
    }

    public void Apply(ChangeOperation operation, OperationExecutionContext context)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // Suppress publishing while applying remote operations to prevent echo-back
        using (PublishingSuppression.Enter())
        {
            operation.Apply(context);
        }
    }

    private OperationExecutionContext CreateDefaultContext()
    {
        return new OperationExecutionContext(Root);
    }
}
