using Beutl.Animation;
using Beutl.Editor.Infrastructure;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.NodeGraph;

namespace Beutl.Editor.Operations;

public sealed class UpdateNodeMemberOperation(
    INodeMember nodeMember,
    string propertyPath,
    object? newValue,
    object? oldValue)
    : ChangeOperation, IPropertyPathProvider, IMergableChangeOperation
{
    public INodeMember NodeMember { get; set; } = nodeMember;

    public string PropertyPath { get; set; } = propertyPath;

    public object? NewValue { get; set; } = newValue;

    public object? OldValue { get; set; } = oldValue;

    private void UpdateValue(object? value)
    {
        switch (PropertyPathHelper.Parse(PropertyPath).Slot)
        {
            case PropertyPathSlot.Animation when NodeMember.Property is IAnimatablePropertyAdapter animatablePropertyAdapter:
                animatablePropertyAdapter.Animation = value as IKeyFrameAnimation;
                return;
            case PropertyPathSlot.Expression when NodeMember.Property is IExpressionPropertyAdapter expressiblePropertyAdapter:
                expressiblePropertyAdapter.Expression = value as IExpression;
                return;
        }

        NodeMember.Property?.SetValue(value);
    }

    public override void Apply()
    {
        UpdateValue(NewValue);
    }

    public override void Revert()
    {
        UpdateValue(OldValue);
    }

    // Mirrors the property adapters: animations and expressions are replaced by reference, values by
    // equality.
    public bool IsNoop => PropertyPathHelper.Parse(PropertyPath).Slot is not PropertyPathSlot.Value
        ? ReferenceEquals(NewValue, OldValue)
        : Equals(NewValue, OldValue);

    public bool TryMerge(ChangeOperation other)
    {
        if (other is not UpdateNodeMemberOperation op)
        {
            return false;
        }

        if (op.NodeMember != NodeMember)
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
