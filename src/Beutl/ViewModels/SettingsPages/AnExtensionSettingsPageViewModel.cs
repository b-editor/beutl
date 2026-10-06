using Beutl.Api.Services;
using Beutl.Controls.Navigation;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Services;
using Beutl.ViewModels.Editors;

using DynamicData;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AnExtensionSettingsPageViewModel : PageContext, IPropertyEditorContextVisitor, IServiceProvider
{
    private readonly PropertyEditorHostServices _services;

    public AnExtensionSettingsPageViewModel(Extension extension, ExtensionProvider extensionProvider)
    {
        Extension = extension;

        var sequenceGenerator = new OperationSequenceGenerator();
        _services = new PropertyEditorHostServices(
            new HistoryManager(extension.Settings!, sequenceGenerator), extensionProvider);

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
        return _services.GetService(serviceType);
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    private void InitializeCoreObject(ExtensionSettings obj, Func<CoreProperty, CorePropertyMetadata, bool>? predicate, ExtensionProvider extensionProvider)
    {
        List<IPropertyAdapter> props = CoreObjectPropertyEditorBuilder.CreateAdapters(obj, predicate);

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

        CoreObjectPropertyEditorBuilder.GroupByDisplayGroupName(tempItems);

        // Put consecutive settings without a named group in a shared frame too,
        // keeping their order relative to the named groups.
        List<IPropertyEditorContext?> ungrouped = [];
        foreach (IPropertyEditorContext? item in tempItems)
        {
            if (item is PropertyEditorGroupContext)
            {
                FlushUngrouped();
                Properties.Add(item);
            }
            else
            {
                ungrouped.Add(item);
            }
        }
        FlushUngrouped();

        void FlushUngrouped()
        {
            if (ungrouped.Count == 0)
                return;

            Properties.Add(new PropertyEditorGroupContext(ungrouped.ToArray(), string.Empty, Properties.Count == 0));
            ungrouped.Clear();
        }
    }
}
