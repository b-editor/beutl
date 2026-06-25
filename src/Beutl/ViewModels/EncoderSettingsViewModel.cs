using System.ComponentModel.DataAnnotations;
using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Services;
using Beutl.Media.Encoding;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.Services.Adapters;
using Beutl.ViewModels.Editors;
using DynamicData;

namespace Beutl.ViewModels;

public sealed class EncoderSettingsViewModel : IPropertyEditorContextVisitor, IServiceProvider, IDisposable
{
    private readonly HistoryManager _history;
    private readonly ExtensionProvider _extensionProvider;
    private PropertyEditorFactoryAdapter? _propertyEditorFactory;
    private PropertiesEditorFactoryImpl? _propertiesEditorFactory;

    public EncoderSettingsViewModel(MediaEncoderSettings settings, ExtensionProvider extensionProvider)
    {
        Settings = settings;
        _extensionProvider = extensionProvider;
        var sequenceGenerator = new OperationSequenceGenerator();
        _history = new HistoryManager(settings, sequenceGenerator);
        InitializeCoreObject(settings, (_, m) => m.Browsable);
    }

    public MediaEncoderSettings Settings { get; }

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(HistoryManager))
        {
            return _history;
        }

        // Expose the session ExtensionProvider and property-editor factories so nested object /
        // list editors resolve them through the service chain even though this host is not an
        // EditViewModel (the former global singleton path is gone).
        if (serviceType == typeof(ExtensionProvider))
            return _extensionProvider;

        if (serviceType.IsAssignableTo(typeof(IPropertyEditorFactory)))
            return _propertyEditorFactory ??= new PropertyEditorFactoryAdapter(_extensionProvider);

        if (serviceType.IsAssignableTo(typeof(IPropertiesEditorFactory)))
            return _propertiesEditorFactory ??= new PropertiesEditorFactoryImpl(_extensionProvider);

        return null;
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    private void InitializeCoreObject(MediaEncoderSettings obj,
        Func<CoreProperty, CorePropertyMetadata, bool>? predicate = null)
    {
        Type objType = obj.GetType();
        Type adapterType = typeof(CorePropertyAdapter<>);

        List<CoreProperty> cprops = [.. PropertyRegistry.GetRegistered(objType)];
        cprops.RemoveAll(x => !(predicate?.Invoke(x, x.GetMetadata<CorePropertyMetadata>(objType)) ?? true));
        List<IPropertyAdapter> props = cprops.ConvertAll(x =>
        {
            CorePropertyMetadata metadata = x.GetMetadata<CorePropertyMetadata>(objType);
            Type adapterGType = adapterType.MakeGenericType(x.PropertyType);
            return (IPropertyAdapter)Activator.CreateInstance(adapterGType, x, obj)!;
        });

        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        IPropertyAdapter[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props, _extensionProvider);
            if (foundItems != null && extension != null)
            {
                if (extension.TryCreateContext(foundItems, out IPropertyEditorContext? context))
                {
                    tempItems.Add(context);
                    context.Accept(this);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);

        tempItems.Sort((x, y) =>
        {
            int xx = GetDisplayAttribute(x)?.GetOrder() ?? 0;
            int yy = GetDisplayAttribute(y)?.GetOrder() ?? 0;
            return xx - yy;
        });

        foreach ((string? Key, IPropertyEditorContext?[] Value) group in tempItems
                     .GroupBy(x => GetDisplayAttribute(x)?.GetGroupName())
                     .Select(x => (x.Key, x.ToArray()))
                     .ToArray())
        {
            if (group.Key != null)
            {
                IPropertyEditorContext?[] array = group.Value;
                if (array.Length >= 1)
                {
                    int index = tempItems.IndexOf(array[0]);
                    tempItems.RemoveMany(array);
                    tempItems.Insert(index, new PropertyEditorGroupContext(array, group.Key, index == 0));
                }
            }
        }

        Properties.AddRange(tempItems);
    }

    private static DisplayAttribute? GetDisplayAttribute(IPropertyEditorContext? context)
    {
        if (context is BaseEditorViewModel { PropertyAdapter: { } adapter })
        {
            return adapter.GetAttributes().FirstOrDefault(i => i is DisplayAttribute) as DisplayAttribute;
        }
        else
        {
            return null;
        }
    }

    public void Dispose()
    {
        for (int i = Properties.Count - 1; i >= 0; i--)
        {
            var item = Properties[i];
            Properties.RemoveAt(i);
            item?.Dispose();
        }
    }
}
