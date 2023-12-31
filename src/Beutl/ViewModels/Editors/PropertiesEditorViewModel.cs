using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Operators.Configure;
using Beutl.Services;

using DynamicData;

namespace Beutl.ViewModels.Editors;

public sealed class PropertiesEditorViewModel : IDisposable, IJsonSerializable
{
    public PropertiesEditorViewModel(ICoreObject obj)
    {
        Target = obj;
        InitializeCoreObject(obj);
    }

    public PropertiesEditorViewModel(ICoreObject obj, Predicate<CorePropertyMetadata> predicate)
    {
        Target = obj;
        InitializeCoreObject(obj, (_, v) => predicate(v));
    }

    public PropertiesEditorViewModel(ICoreObject obj, Func<CoreProperty, CorePropertyMetadata, bool> predicate)
    {
        Target = obj;
        InitializeCoreObject(obj, predicate);
    }

    public ICoreObject Target { get; }

    public CoreList<IPropertyEditorContext> Properties { get; } = [];

    public void Dispose()
    {
        foreach (IPropertyEditorContext item in Properties.GetMarshal().Value)
        {
            item.Dispose();
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue(nameof(Properties), out JsonNode? propsNode)
            && propsNode is JsonArray propsArray)
        {
            foreach ((JsonNode? node, IPropertyEditorContext? context) in propsArray.Zip(Properties))
            {
                if (context != null && node != null)
                {
                    context.ReadFromJson(node.AsObject());
                }
            }
        }
    }

    public void WriteToJson(JsonObject json)
    {
        var array = new JsonArray();

        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            if (item == null)
            {
                array.Add(null);
            }
            else
            {
                var node = new JsonObject();
                item.WriteToJson(node);
                array.Add(node);
            }
        }

        json[nameof(Properties)] = array;
    }

    private void InitializeCoreObject(ICoreObject obj, Func<CoreProperty, CorePropertyMetadata, bool>? predicate = null)
    {
        Type objType = obj.GetType();
        Type wrapperType = typeof(CorePropertyImpl<>);
        Type animatableWrapperType = typeof(AnimatableCorePropertyImpl<>);
        bool isAnimatable = obj is IAnimatable;

        List<CoreProperty> cprops = [.. PropertyRegistry.GetRegistered(objType)];
        cprops.RemoveAll(x => !(predicate?.Invoke(x, x.GetMetadata<CorePropertyMetadata>(objType)) ?? true));
        List<IAbstractProperty> props = cprops.ConvertAll(x =>
        {
            CorePropertyMetadata metadata = x.GetMetadata<CorePropertyMetadata>(objType);
            Type wtype = isAnimatable ? animatableWrapperType : wrapperType;
            Type wrapperGType = wtype.MakeGenericType(x.PropertyType);
            return (IAbstractProperty)Activator.CreateInstance(wrapperGType, x, obj)!;
        });
        Properties.EnsureCapacity(props.Count);
        IAbstractProperty[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props);
            if (foundItems != null && extension != null)
            {
                if (extension.TryCreateContext(foundItems, out IPropertyEditorContext? context))
                {
                    Properties.Add(context);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);
    }
}
