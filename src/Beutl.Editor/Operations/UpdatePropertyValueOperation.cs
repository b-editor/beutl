using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Serialization;

using Beutl.Editor.Infrastructure;

namespace Beutl.Editor.Operations;

public sealed class UpdatePropertyValueOperation(Guid objectId, string propertyPath, JsonNode? value)
    : ChangeOperation, IPropertyPathProvider
{
    public static UpdatePropertyValueOperation Create(
        ICoreObject obj,
        string propertyPath,
        object? value,
        OperationSequenceGenerator sequenceNumberGenerator)
    {
        JsonNode? jsonNode = value != null ? CoreSerializer.SerializeToJsonNode(value) : null;

        return new UpdatePropertyValueOperation(obj.Id, propertyPath, jsonNode)
        {
            SequenceNumber = sequenceNumberGenerator.GetNext()
        };
    }

    public Guid ObjectId { get; set; } = objectId;

    public string PropertyPath { get; set; } = propertyPath;

    public JsonNode? Value { get; set; } = value;

    public override void Apply(OperationExecutionContext context)
    {
        var obj = context.FindObject(ObjectId)
                  ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        string name = PropertyPath;
        bool updateAnimation = false;

        if (PropertyPath.Contains('.'))
        {
            var parts = PropertyPath.Split('.');

            if (parts[^1] == "Animation" && parts.Length >= 2)
            {
                name = parts[^2];
                updateAnimation = true;
            }
            else
            {
                name = parts[^1];
                updateAnimation = false;
            }
        }

        var coreProperty = PropertyRegistry.FindRegistered(obj.GetType(), name);

        if (coreProperty != null)
        {
            ApplyToCoreProperty(obj, coreProperty);
            return;
        }

        if (obj is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {obj.GetType().FullName}.");

            ApplyToEngineProperty(engineObj, engineProperty, updateAnimation);
        }
    }

    private void ApplyToEngineProperty(EngineObject engineObject, IProperty engineProperty, bool updateAnimation)
    {
        if (updateAnimation)
        {
            if (engineProperty.IsAnimatable)
            {
                IAnimation? animation = Value != null
                    ? CoreSerializer.DeserializeFromJsonNode(Value, typeof(IAnimation)) as IAnimation
                    : null;
                engineProperty.Animation = animation;
            }
        }
        else
        {
            var json = new JsonObject { [engineProperty.Name] = Value?.DeepClone() };
            var options = new CoreSerializerOptions
            {
                BaseUri = engineObject.EnumerateAncestors<CoreObject>().FirstOrDefault(o => o.Uri != null)?.Uri
            };
            var context = new JsonSerializationContext(engineObject.GetType(), options: options, json: json);

            using (ThreadLocalSerializationContext.Enter(context))
            {
                engineProperty.DeserializeValue(context);
            }
        }
    }

    private void ApplyToCoreProperty(ICoreObject obj, CoreProperty coreProperty)
    {
        var json = new JsonObject { [coreProperty.Name] = Value?.DeepClone() };
        var options = new CoreSerializerOptions
        {
            BaseUri = (obj as IHierarchical)?.EnumerateAncestors<CoreObject>().FirstOrDefault(o => o.Uri != null)?.Uri
        };
        var context = new JsonSerializationContext(obj.GetType(), options: options, json: json);

        using (ThreadLocalSerializationContext.Enter(context))
        {
            Optional<object?> deserialized = coreProperty.RouteDeserialize(context);

            if (deserialized.HasValue)
            {
                if (deserialized.Value is IReference { IsNull: false } reference)
                {
                    context.Resolve(reference.Id,
                        resolved => obj.SetValue(coreProperty, reference.Resolved((CoreObject)resolved)));
                }

                obj.SetValue(coreProperty, deserialized.Value);
            }
        }
    }

    public override ChangeOperation CreateRevertOperation(OperationExecutionContext context, OperationSequenceGenerator sequenceGenerator)
    {
        var obj = context.FindObject(ObjectId)
                  ?? throw new InvalidOperationException($"Object with ID {ObjectId} not found.");

        // Get the current value from the object and serialize it
        var currentValue = GetCurrentValue(obj);
        var currentValueJson = currentValue != null ? CoreSerializer.SerializeToJsonNode(currentValue) : null;

        return new UpdatePropertyValueOperation(ObjectId, PropertyPath, currentValueJson)
        {
            SequenceNumber = sequenceGenerator.GetNext()
        };
    }

    private object? GetCurrentValue(ICoreObject obj)
    {
        string name = PropertyPath;
        bool getAnimation = false;

        if (PropertyPath.Contains('.'))
        {
            var parts = PropertyPath.Split('.');

            if (parts[^1] == "Animation" && parts.Length >= 2)
            {
                name = parts[^2];
                getAnimation = true;
            }
            else
            {
                name = parts[^1];
                getAnimation = false;
            }
        }

        var coreProperty = PropertyRegistry.FindRegistered(obj.GetType(), name);

        if (coreProperty != null)
        {
            return obj.GetValue(coreProperty);
        }

        if (obj is EngineObject engineObj)
        {
            var engineProperty = engineObj.Properties.FirstOrDefault(p => p.Name == name)
                                 ?? throw new InvalidOperationException(
                                     $"Engine property {PropertyPath} not found on type {obj.GetType().FullName}.");

            if (getAnimation)
            {
                return engineProperty.Animation;
            }
            else
            {
                return engineProperty.CurrentValue;
            }
        }

        throw new InvalidOperationException($"Property {PropertyPath} not found on type {obj.GetType().FullName}.");
    }
}
