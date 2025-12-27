using Beutl.Animation.Easings;

namespace Beutl.Editor.Operations;

public sealed class UpdateSplineEasingOperation(
    SplineEasing easing,
    string propertyPath,
    float newValue,
    float oldValue)
    : ChangeOperation, IPropertyPathProvider, IMergableChangeOperation
{
    public CoreObject? Parent { get; set; }

    public SplineEasing Easing { get; set; } = easing;

    public string PropertyPath { get; set; } = propertyPath;

    public float NewValue { get; set; } = newValue;

    public float OldValue { get; set; } = oldValue;

    public override void Apply(OperationExecutionContext context)
    {
        SetValue(Easing, PropertyPath, NewValue);
    }

    public override void Revert(OperationExecutionContext context)
    {
        SetValue(Easing, PropertyPath, OldValue);
    }

    private static void SetValue(SplineEasing easing, string propertyPath, float value)
    {
        string propertyName = propertyPath.Contains('.')
            ? propertyPath.Split('.')[^1]
            : propertyPath;

        switch (propertyName)
        {
            case nameof(SplineEasing.X1):
                easing.X1 = value;
                break;
            case nameof(SplineEasing.Y1):
                easing.Y1 = value;
                break;
            case nameof(SplineEasing.X2):
                easing.X2 = value;
                break;
            case nameof(SplineEasing.Y2):
                easing.Y2 = value;
                break;
            default:
                throw new ArgumentException($"Unknown property name: {propertyName}", nameof(propertyPath));
        }
    }

    public bool TryMerge(ChangeOperation other)
    {
        if (other is not UpdateSplineEasingOperation op)
        {
            return false;
        }

        if (op.Easing != Easing)
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
