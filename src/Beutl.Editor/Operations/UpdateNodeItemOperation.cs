using Beutl.Animation;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.NodeTree;

namespace Beutl.Editor.Operations;

public sealed class UpdateNodeItemOperation(
    INodeItem nodeItem,
    string propertyPath,
    object? newValue,
    object? oldValue)
    : ChangeOperation, IPropertyPathProvider, IMergableChangeOperation
{
    public INodeItem NodeItem { get; set; } = nodeItem;

    public string PropertyPath { get; set; } = propertyPath;

    public object? NewValue { get; set; } = newValue;

    public object? OldValue { get; set; } = oldValue;

    public override void Apply(OperationExecutionContext context)
    {
        if (PropertyPath.Contains('.'))
        {
            string[] parts = PropertyPath.Split('.');

            switch (parts[^1])
            {
                case "Animation" when parts.Length >= 2 && NodeItem.Property is IAnimatablePropertyAdapter animatablePropertyAdapter:
                    animatablePropertyAdapter.Animation = NewValue as IAnimation;
                    return;
                case "Expression" when parts.Length >= 2 && NodeItem.Property is IExpressionPropertyAdapter expressiblePropertyAdapter:
                    expressiblePropertyAdapter.Expression = NewValue as IExpression;
                    return;
            }
        }

        NodeItem.Property?.SetValue(NewValue);
    }

    public override void Revert(OperationExecutionContext context)
    {
        if (PropertyPath.Contains('.'))
        {
            string[] parts = PropertyPath.Split('.');

            switch (parts[^1])
            {
                case "Animation" when parts.Length >= 2 && NodeItem.Property is IAnimatablePropertyAdapter animatablePropertyAdapter:
                    animatablePropertyAdapter.Animation = OldValue as IAnimation;
                    return;
                case "Expression" when parts.Length >= 2 && NodeItem.Property is IExpressionPropertyAdapter expressiblePropertyAdapter:
                    expressiblePropertyAdapter.Expression = OldValue as IExpression;
                    return;
            }
        }

        NodeItem.Property?.SetValue(OldValue);
    }

    public bool TryMerge(ChangeOperation other)
    {
        if (other is not UpdateNodeItemOperation op)
        {
            return false;
        }

        if (op.NodeItem != NodeItem)
        {
            return false;
        }

        if (op.PropertyPath != PropertyPath)
        {
            return false;
        }

        NewValue = op.NewValue;
        return true;
    }
}
