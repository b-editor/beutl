using Beutl.Editor.Infrastructure;
using Beutl.Engine;
using Beutl.NodeTree;

namespace Beutl.Editor.Operations;

public abstract class CollectionChangeOperation<T> : ChangeOperation, IPropertyPathProvider
{
    public required CoreObject Object { get; set; }

    public required string PropertyPath { get; set; }

    private IList<T> VerifyType(object obj, object? list)
    {
        if (list is not IList<T> list2)
        {
            throw new InvalidOperationException(
                $"Property {PropertyPath} is not a list on type {obj.GetType().FullName}.");
        }

        return list2;
    }

    private IListProperty<T> FindListProperty(EngineObject engineObj, string name)
    {
        var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                             ?? throw new InvalidOperationException(
                                 $"Engine property {PropertyPath} not found on type {engineObj.GetType().FullName}.");

        if (engineProperty is not IListProperty<T> listProperty)
        {
            throw new InvalidOperationException(
                $"Engine property {PropertyPath} is not a list on type {engineObj.GetType().FullName}.");
        }

        return listProperty;
    }

    public override void Apply(OperationExecutionContext context)
    {
        var type = Object.GetType();
        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(type, name);

        if (coreProperty != null)
        {
            ApplyTo(VerifyType(Object, Object.GetValue(coreProperty)));
            return;
        }

        if (Object is INodeItem nodeItem && name == "Property")
        {
            ApplyTo(VerifyType(nodeItem, nodeItem.Property?.GetValue()));
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var listProperty = FindListProperty(engineObj, name);
            ApplyToEngineProperty(listProperty);
        }
    }

    protected abstract void ApplyToEngineProperty(IListProperty<T> listProperty);

    protected abstract void ApplyTo(IList<T> list);

    public override void Revert(OperationExecutionContext context)
    {
        var type = Object.GetType();
        var name = PropertyPathHelper.GetPropertyNameFromPath(PropertyPath);
        var coreProperty = PropertyRegistry.FindRegistered(type, name);

        if (coreProperty != null)
        {
            RevertTo(VerifyType(Object, Object.GetValue(coreProperty)));
            return;
        }

        if (Object is INodeItem nodeItem && name == "Property")
        {
            RevertTo(VerifyType(nodeItem, nodeItem.Property?.GetValue()));
            return;
        }

        if (Object is EngineObject engineObj)
        {
            var listProperty = FindListProperty(engineObj, name);
            RevertToEngineProperty(listProperty);
        }
    }

    protected abstract void RevertToEngineProperty(IListProperty<T> listProperty);

    protected abstract void RevertTo(IList<T> list);
}
