using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.Editor.Operations;

public sealed class UpdatePropertyValueOperation<T>(CoreObject obj, string propertyPath, T newValue, T oldValue)
    : ChangeOperation, IPropertyPathProvider, IMergableChangeOperation
{
    public CoreObject Object { get; set; } = obj;

    public string PropertyPath { get; set; } = propertyPath;

    public T NewValue { get; set; } = newValue;

    public T OldValue { get; set; } = oldValue;

    public override void Apply(OperationExecutionContext context)
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
            if (parts[^1] == "Expression" && parts.Length >= 2)
            {
                name = parts[^2];
                updateExpression = true;
            }
            else
            {
                name = parts[^1];
            }
        }

        var coreProperty = PropertyRegistry.FindRegistered(Object.GetType(), name);

        if (coreProperty != null)
        {
            ApplyToCoreProperty(coreProperty);
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {engineObj.GetType().FullName}.");

            ApplyToEngineProperty(engineProperty, updateAnimation, updateExpression);
        }
    }

    private void ApplyToEngineProperty(IProperty engineProperty, bool updateAnimation, bool updateExpression)
    {
        if (updateAnimation)
        {
            if (engineProperty.IsAnimatable)
            {
                engineProperty.Animation = NewValue as IAnimation<T>;
            }
        }
        else if (updateExpression)
        {
            if (engineProperty.IsAnimatable)
            {
                engineProperty.Expression = NewValue as IExpression<T>;
            }
        }
        else
        {
            if (engineProperty is IProperty<T> typedProperty)
            {
                typedProperty.CurrentValue = NewValue;
            }
        }
    }

    private void ApplyToCoreProperty(CoreProperty coreProperty)
    {
        Object.SetValue(coreProperty, NewValue);
    }

    public override void Revert(OperationExecutionContext context)
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

        var coreProperty = PropertyRegistry.FindRegistered(Object.GetType(), name);

        if (coreProperty != null)
        {
            RevertToCoreProperty(coreProperty);
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {engineObj.GetType().FullName}.");

            RevertToEngineProperty(engineProperty, updateAnimation, updateExpression);
        }
    }

    private void RevertToEngineProperty(IProperty engineProperty, bool updateAnimation, bool updateExpression)
    {
        if (updateAnimation)
        {
            if (engineProperty.IsAnimatable)
            {
                engineProperty.Animation = OldValue as IAnimation<T>;
            }
        }
        else if (updateExpression)
        {
            if (engineProperty.IsAnimatable)
            {
                engineProperty.Expression = OldValue as IExpression<T>;
            }
        }
        else
        {
            if (engineProperty is IProperty<T> typedProperty)
            {
                typedProperty.CurrentValue = OldValue;
            }
        }
    }

    private void RevertToCoreProperty(CoreProperty coreProperty)
    {
        Object.SetValue(coreProperty, OldValue);
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
