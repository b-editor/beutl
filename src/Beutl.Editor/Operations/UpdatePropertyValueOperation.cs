using Beutl.Animation;
using Beutl.Editor.Infrastructure;
using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.Editor.Operations;

public sealed class UpdatePropertyValueOperation<T>(CoreObject obj, string propertyPath, T newValue, T oldValue)
    : ChangeOperation, IPropertyPathProvider, IMergableChangeOperation, IUpdatePropertyValueOperation
{
    public CoreObject Object { get; set; } = obj;

    public string PropertyPath { get; set; } = propertyPath;

    public T NewValue { get; set; } = newValue;

    public T OldValue { get; set; } = oldValue;

    object? IUpdatePropertyValueOperation.NewValue => NewValue;

    object? IUpdatePropertyValueOperation.OldValue => OldValue;

    private (string Name, bool UpdateAnimation, bool UpdateExpression) ParsePropertyPath()
    {
        (string name, PropertyPathSlot slot) = PropertyPathHelper.Parse(PropertyPath);
        return (name, slot == PropertyPathSlot.Animation, slot == PropertyPathSlot.Expression);
    }

    private static void UpdateEngineProperty(IProperty engineProperty, bool updateAnimation, bool updateExpression, T value)
    {
        if (updateAnimation)
        {
            if (engineProperty is IExpressionProperty { IsAnimatable: true })
            {
                engineProperty.SetKeyFrameAnimation(value as IKeyFrameAnimation);
            }
        }
        else if (updateExpression)
        {
            // The recorded chain is restored as it was, keyframes included.
            if (engineProperty is IExpressionChainHost host)
            {
                host.SetExpressionChain(value as IExpression);
            }
            else if (engineProperty is IExpressionProperty expressionProperty)
            {
                expressionProperty.Expression = value as IExpression;
            }
        }
        else
        {
            if (engineProperty is IProperty<T> typedProperty)
            {
                typedProperty.Value = value;
            }
        }
    }

    private void SetValue(T value)
    {
        var (name, updateAnimation, updateExpression) = ParsePropertyPath();

        var engineProperty = Object.Properties.FirstOrDefault(p => p.Name == name);
        if (engineProperty is null)
        {
            // A plain CoreObject without the property is skipped.
            if (Object is not EngineObject) return;
            throw new InvalidOperationException(
                $"Engine property {PropertyPath} not found on type {Object.GetType().FullName}.");
        }

        UpdateEngineProperty(engineProperty, updateAnimation, updateExpression, value);
    }

    public override void Apply() => SetValue(NewValue);

    public override void Revert() => SetValue(OldValue);

    // Mirrors the setters SetValue goes through: an engine property replaces its animation or
    // expression by reference, and a value by the property's replacement rule.
    public bool IsNoop
    {
        get
        {
            var (_, updateAnimation, updateExpression) = ParsePropertyPath();
            return updateAnimation || updateExpression
                ? ReferenceEquals(NewValue, OldValue)
                : !ValueReplacement.RequiresReplacement(OldValue, NewValue, replaceEquivalent: false);
        }
    }

    public bool TryMerge(ChangeOperation other)
    {
        if (other is not UpdatePropertyValueOperation<T> op) return false;
        if (op.Object != Object) return false;
        if (op.PropertyPath != PropertyPath) return false;

        NewValue = op.NewValue;
        return true;

    }
}
