using Beutl.Controls.Navigation;
using Beutl.Operation;
using Beutl.Services;
using Beutl.ViewModels.Editors;

using DynamicData;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AnExtensionSettingsPageViewModel : PageContext, IPropertyEditorContextVisitor, IServiceProvider
{
    private readonly CommandRecorder _recorder = new();

    public AnExtensionSettingsPageViewModel(Extension extension)
    {
        Extension = extension;
        InitializeCoreObject(extension.Settings!, (_, m) => m.Browsable);

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
        if (serviceType == typeof(CommandRecorder))
        {
            return _recorder;
        }

        return null;
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    private void InitializeCoreObject(ExtensionSettings obj, Func<CoreProperty, CorePropertyMetadata, bool>? predicate = null)
    {
        Type objType = obj.GetType();
        Type wrapperType = typeof(CorePropertyImpl<>);

        List<CoreProperty> cprops = [.. PropertyRegistry.GetRegistered(objType)];
        cprops.RemoveAll(x => !(predicate?.Invoke(x, x.GetMetadata<CorePropertyMetadata>(objType)) ?? true));
        List<IAbstractProperty> props = cprops.ConvertAll(x =>
        {
            CorePropertyMetadata metadata = x.GetMetadata<CorePropertyMetadata>(objType);
            Type wrapperGType = wrapperType.MakeGenericType(x.PropertyType);
            return (IAbstractProperty)Activator.CreateInstance(wrapperGType, x, obj)!;
        });

        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        IAbstractProperty[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props);
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
            if (x is BaseEditorViewModel { WrappedProperty: { } abProperty }
                && abProperty.GetCoreProperty() is { } coreProperty
                && coreProperty.TryGetMetadata(abProperty.ImplementedType, out CorePropertyMetadata? metadata))
            {
                return metadata.DisplayAttribute?.GetGroupName();
            }
            else
            {
                return null;
            }
        })
            .Select(x => (x.Key, x.ToArray())))
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
