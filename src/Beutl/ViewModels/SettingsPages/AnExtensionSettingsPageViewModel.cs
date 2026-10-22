using Beutl.Api.Services;
using Beutl.Controls.Navigation;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;


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
            new HistoryManager(sequenceGenerator), extensionProvider);

        InitializeCoreObject(extension.Settings!, extensionProvider);

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

    private void InitializeCoreObject(ExtensionSettings obj, ExtensionProvider extensionProvider)
    {
        List<IPropertyAdapter> props = CoreObjectPropertyEditorBuilder.CreateSettingsAdapters(obj);

        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        CoreObjectPropertyEditorBuilder.CreateContexts(props, extensionProvider, context =>
        {
            tempItems.Add(context);
            context.Accept(this);
        }, forSettings: true);

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
