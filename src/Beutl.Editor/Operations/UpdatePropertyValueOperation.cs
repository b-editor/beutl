using Beutl.Animation;
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
        string name = PropertyPath;
        bool updateAnimation = false;
        bool updateExpression = false;

        if (PropertyPath.Contains('.'))
        {
            var parts = PropertyPath.Split('.');

            if (parts[^1] == "Animation" && parts.Length >= 2)
            {
                name = parts[^2];
                updateAnimation = true;
            }
            else if (parts[^1] == "Expression" && parts.Length >= 2)
            {
                name = parts[^2];
                updateExpression = true;
            }
            else
            {
                name = parts[^1];
            }
        }

        return (name, updateAnimation, updateExpression);
    }

    private static void UpdateEngineProperty(IProperty engineProperty, bool updateAnimation, bool updateExpression, T value)
    {
        if (updateAnimation)
        {
            if (engineProperty.IsAnimatable)
            {
                engineProperty.Animation = value as IAnimation;
            }
        }
        else if (updateExpression)
        {
            if (engineProperty.IsAnimatable)
            {
                engineProperty.Expression = value as IExpression;
            }
        }
        else
        {
            if (engineProperty is IProperty<T> typedProperty)
            {
                typedProperty.CurrentValue = value;
            }
        }
    }

    public override void Apply(OperationExecutionContext context)
    {
        var (name, updateAnimation, updateExpression) = ParsePropertyPath();

        var coreProperty = PropertyRegistry.FindRegistered(Object.GetType(), name);

        if (coreProperty != null)
        {
            Object.SetValue(coreProperty, NewValue);
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {engineObj.GetType().FullName}.");

            UpdateEngineProperty(engineProperty, updateAnimation, updateExpression, NewValue);
        }
    }

    public override void Revert(OperationExecutionContext context)
    {
        var (name, updateAnimation, updateExpression) = ParsePropertyPath();

        var coreProperty = PropertyRegistry.FindRegistered(Object.GetType(), name);

        if (coreProperty != null)
        {
            Object.SetValue(coreProperty, OldValue);
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {engineObj.GetType().FullName}.");

            UpdateEngineProperty(engineProperty, updateAnimation, updateExpression, OldValue);
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
