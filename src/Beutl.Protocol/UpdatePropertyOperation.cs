using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.Protocol;

public class UpdatePropertyOperation(Guid objectId, string propertyName, JsonNode? value) : OperationBase
{
    public static UpdatePropertyOperation Create(ICoreObject obj, IProperty property, string name, Type type, object? value, SequenceNumberGenerator sequenceNumberGenerator)
    {
        var simJson = new JsonObject();
        var innerContext = new JsonSerializationContext(obj.GetType(), NullSerializationErrorNotifier.Instance, null, simJson);
        using (ThreadLocalSerializationContext.Enter(innerContext))
        {
            innerContext.SetValue(property.Name, value, type);
        }
        JsonNode? jsonNode = simJson[property.Name];
        simJson[property.Name] = null;

        return new UpdatePropertyOperation(obj.Id, name, jsonNode)
        {
            SequenceNumber = sequenceNumberGenerator.GetNext()
        };
    }

    public static UpdatePropertyOperation Create(ICoreObject obj, CoreProperty property, object? value, SequenceNumberGenerator sequenceNumberGenerator)
    {
        var simJson = new JsonObject();
        var innerContext = new JsonSerializationContext(obj.GetType(), NullSerializationErrorNotifier.Instance, null, simJson);
        using (ThreadLocalSerializationContext.Enter(innerContext))
        {
            innerContext.SetValue(property.Name, value, property.PropertyType);
        }
        JsonNode? jsonNode = simJson[property.Name];
        simJson[property.Name] = null;

        return new UpdatePropertyOperation(obj.Id, property.Name, jsonNode)
        {
            SequenceNumber = sequenceNumberGenerator.GetNext()
        };
    }

    public Guid ObjectId { get; set; } = objectId;

    public string PropertyName { get; set; } = propertyName;

    public JsonNode? Value { get; set; } = value;

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
            string name = PropertyName;
            bool updateAnimation = false;
            if (PropertyName.Contains('.'))
            {
                var parts = PropertyName.Split('.');
                if (parts.Length > 2)
                    throw new InvalidOperationException($"Nested properties deeper than one level are not supported: {PropertyName}.");
                if (parts[1] != "Animation")
                    throw new InvalidOperationException($"Only animation properties are supported for nested properties: {PropertyName}.");
                name = parts[0];
                updateAnimation = true;
            }

            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                ?? throw new InvalidOperationException($"Engine property {PropertyName} not found on type {obj.GetType().FullName}.");

            ExecuteEngineProperty(engineObj, engineProperty, updateAnimation);
            return;
        }
    }

    private void ExecuteEngineProperty(EngineObject engineObject, IProperty engineProperty, bool updateAnimation)
    {
        var json = new JsonObject
        {
            [engineProperty.Name] = Value?.DeepClone()
        };
        var context = new JsonSerializationContext(engineObject.GetType(), NullSerializationErrorNotifier.Instance, null, json);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            if (updateAnimation)
            {
                if (engineProperty.IsAnimatable)
                {
                    IAnimation? animation = context.GetValue<IAnimation>(engineProperty.Name);
                    engineProperty.Animation = animation;
                }
            }
            else
            {
                engineProperty.DeserializeValue(context);
            }
        }
    }

    private void ExecuteCoreProperty(ICoreObject obj, CoreProperty coreProperty)
    {
        var json = new JsonObject
        {
            [coreProperty.Name] = Value?.DeepClone()
        };
        var context = new JsonSerializationContext(obj.GetType(), NullSerializationErrorNotifier.Instance, null, json);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            Optional<object?> value = coreProperty.RouteDeserialize(context);
            if (value.HasValue)
            {
                if (value.Value is IReference { IsNull: false } reference)
                {
                    context.Resolve(reference.Id,
                        resolved => obj.SetValue(coreProperty, reference.Resolved((CoreObject)resolved)));
                }

                obj.SetValue(coreProperty, value.Value);
            }
        }
    }
}
