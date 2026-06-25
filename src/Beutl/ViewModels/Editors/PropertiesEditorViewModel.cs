using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Engine;
using Beutl.PropertyAdapters;
using Beutl.Services;
using DynamicData;

namespace Beutl.ViewModels.Editors;

public sealed class PropertiesEditorViewModel : IDisposable, IJsonSerializable, IPropertiesEditorViewModel
{
    private readonly ExtensionProvider _extensionProvider;

    public PropertiesEditorViewModel(ICoreObject obj, ExtensionProvider extensionProvider)
    {
        _extensionProvider = extensionProvider;
        Target = obj;
        if (obj is EngineObject engineObject)
        {
            InitializeEngineObject(engineObject);
        }
        else
        {
            InitializeCoreObject(obj);
        }
    }

    public PropertiesEditorViewModel(ICoreObject obj, ExtensionProvider extensionProvider, Predicate<CorePropertyMetadata> predicate)
    {
        _extensionProvider = extensionProvider;
        Target = obj;
        InitializeCoreObject(obj, (_, v) => predicate(v));
    }

    public PropertiesEditorViewModel(ICoreObject obj, ExtensionProvider extensionProvider, Func<CoreProperty, CorePropertyMetadata, bool> predicate)
    {
        _extensionProvider = extensionProvider;
        Target = obj;
        InitializeCoreObject(obj, predicate);
    }

    public PropertiesEditorViewModel(EngineObject obj, ExtensionProvider extensionProvider, Func<IProperty, bool> predicate)
    {
        _extensionProvider = extensionProvider;
        Target = obj;
        InitializeEngineObject(obj, predicate);
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

    private void InitializeEngineObject(EngineObject obj, Func<IProperty, bool>? predicate = null)
    {
        Type adapterType = typeof(EnginePropertyAdapter<>);
        Type simpleAdapterType = typeof(SimplePropertyAdapter<>);
        Type animatableAdapterType = typeof(AnimatablePropertyAdapter<>);

        List<IProperty> cprops = [.. obj.GetDisplayProperties()];
        cprops.RemoveAll(x => !(predicate?.Invoke(x) ?? true));
        List<IPropertyAdapter> props = cprops.ConvertAll(x =>
        {
            Type determinedType = x.IsAnimatable
                ? animatableAdapterType
                : x.SupportsExpression
                    ? simpleAdapterType
                    : adapterType;
            Type adapterGType = determinedType.MakeGenericType(x.ValueType);
            return (IPropertyAdapter)Activator.CreateInstance(adapterGType, x, obj)!;
        });
        Properties.EnsureCapacity(props.Count);
        IPropertyAdapter[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props, _extensionProvider);
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

    private void InitializeCoreObject(ICoreObject obj, Func<CoreProperty, CorePropertyMetadata, bool>? predicate = null)
    {
        Type objType = obj.GetType();
        Type adapterType = typeof(CorePropertyAdapter<>);

        List<CoreProperty> cprops = [.. PropertyRegistry.GetRegistered(objType)];
        cprops.RemoveAll(x => !(predicate?.Invoke(x, x.GetMetadata<CorePropertyMetadata>(objType)) ?? true));
        List<IPropertyAdapter> props = cprops.ConvertAll(x =>
        {
            Type determinedType = adapterType;
            Type adapterGType = determinedType.MakeGenericType(x.PropertyType);
            return (IPropertyAdapter)Activator.CreateInstance(adapterGType, x, obj)!;
        });
        Properties.EnsureCapacity(props.Count);
        IPropertyAdapter[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props, _extensionProvider);
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
