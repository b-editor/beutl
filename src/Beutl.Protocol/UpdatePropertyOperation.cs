using Beutl.Engine;

namespace Beutl.Protocol;

public class UpdatePropertyOperation<T>(Guid objectId, string propertyName, T? value) : OperationBase
{
    public required Guid ObjectId { get; set; } = objectId;

    public required string PropertyName { get; set; } = propertyName;

    public required T? Value { get; set; } = value;

    public override void Execute(OperationContext context)
    {
        var obj = context.FindObject(ObjectId) ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        var coreProperty = PropertyRegistry.FindRegistered(obj.GetType(), PropertyName);

        if (coreProperty != null)
        {
            ExecuteCoreProperty(obj, coreProperty);
            return;
        }

        if (obj is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == PropertyName)
                ?? throw new InvalidOperationException($"Engine property {PropertyName} not found on type {obj.GetType().FullName}.");
            if (engineProperty is not IProperty<T> typedProperty)
            {
                throw new InvalidOperationException($"Engine property {PropertyName} is not of type {typeof(T).FullName} on type {obj.GetType().FullName}.");
            }

            ExecuteEngineProperty(typedProperty);
            return;
        }
    }

    private void ExecuteEngineProperty(IProperty<T> engineProperty)
    {
        engineProperty.CurrentValue = Value!;
    }

    private void ExecuteCoreProperty(ICoreObject obj, CoreProperty coreProperty)
    {
        obj.SetValue(coreProperty, Value!);
    }
}
