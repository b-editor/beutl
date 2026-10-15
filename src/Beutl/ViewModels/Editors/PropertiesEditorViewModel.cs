using System.Text.Json.Nodes;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.PropertyAdapters;

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
        foreach (IPropertyEditorContext item in Properties.AsSpan())
        {
            item.Dispose();
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        NestedEditorContextHelper.ReadEditorArray(json, nameof(Properties), Properties);
    }

    public void WriteToJson(JsonObject json)
    {
        NestedEditorContextHelper.WriteEditorArray(json, nameof(Properties), Properties);
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
        AddContexts(props);
    }

    private void InitializeCoreObject(ICoreObject obj)
    {
        AddContexts(CoreObjectPropertyEditorBuilder.CreateAdapters(obj, null));
    }

    private void AddContexts(List<IPropertyAdapter> props)
    {
        Properties.EnsureCapacity(props.Count);
        CoreObjectPropertyEditorBuilder.CreateContexts(props, _extensionProvider, Properties.Add);
    }
}
