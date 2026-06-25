using System.ComponentModel.DataAnnotations;
using Beutl.Api.Services;
using Beutl.Controls.Navigation;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.Services.Adapters;
using Beutl.ViewModels.Editors;

using DynamicData;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AnExtensionSettingsPageViewModel : PageContext, IPropertyEditorContextVisitor, IServiceProvider
{
    private readonly HistoryManager _history;
    private readonly ExtensionProvider _extensionProvider;
    private PropertyEditorFactoryAdapter? _propertyEditorFactory;
    private PropertiesEditorFactoryImpl? _propertiesEditorFactory;

    public AnExtensionSettingsPageViewModel(Extension extension, ExtensionProvider extensionProvider)
    {
        Extension = extension;
        _extensionProvider = extensionProvider;

        var sequenceGenerator = new OperationSequenceGenerator();
        _history = new HistoryManager(extension.Settings!, sequenceGenerator);

        InitializeCoreObject(extension.Settings!, (_, m) => m.Browsable, extensionProvider);

        NavigateParent.Subscribe(async () =>
        {
            INavigationProvider nav = await GetNavigation();
            await nav.NavigateAsync<ExtensionsSettingsPageViewModel>();
        });
    }

    public Extension Extension { get; }

    public AsyncReactiveCommand NavigateParent { get; } = new();

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(HistoryManager))
        {
            return _history;
        }

        // Expose the session ExtensionProvider and property-editor factories so nested object /
        // list editors resolve them through the service chain even though this host is not an
        // EditViewModel.
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

    private void InitializeCoreObject(ExtensionSettings obj, Func<CoreProperty, CorePropertyMetadata, bool>? predicate, ExtensionProvider extensionProvider)
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
            (foundItems, extension) = PropertyEditorService.MatchProperty(props, extensionProvider);
            if (foundItems != null && extension != null)
            {
                if (extension.TryCreateContextForSettings(foundItems, out IPropertyEditorContext? context))
                {
                    tempItems.Add(context);
                    context.Accept(this);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);

        foreach ((string? Key, IPropertyEditorContext?[] Value) group in tempItems.GroupBy(x =>
        {
            if (x is BaseEditorViewModel { PropertyAdapter: { } adapter })
            {
                return (adapter.GetAttributes().FirstOrDefault(i => i is DisplayAttribute) as DisplayAttribute)
                    ?.GetGroupName();
            }
            else
            {
                return null;
            }
        })
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
}
