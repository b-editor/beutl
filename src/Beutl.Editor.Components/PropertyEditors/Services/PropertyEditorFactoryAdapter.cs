using Beutl.Api.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;

namespace Beutl.Editor.Components.PropertyEditors.Services;

internal sealed class PropertyEditorFactoryAdapter(ExtensionProvider extensionProvider) : IPropertyEditorFactory
{
    public IPropertyEditorContext? CreateEditor(IPropertyAdapter property)
        => PropertyEditorResolver.CreateContext(property, Extensions, PropertyEditorSurface.Panel, extensionProvider.Notifications);

    public IPropertyEditorContext? CreateNodeEditor(IPropertyAdapter property)
        => PropertyEditorResolver.CreateContext(property, Extensions, PropertyEditorSurface.Node, extensionProvider.Notifications);

    public IPropertyEditorContext? CreateListItemEditor(IPropertyAdapter property)
        => PropertyEditorResolver.CreateContext(property, Extensions, PropertyEditorSurface.ListItem, extensionProvider.Notifications);

    public IReadOnlyList<IPropertyEditorContext?> CreatePropertyEditorContexts(
        IReadOnlyList<IPropertyAdapter> properties,
        IPropertyEditorContextVisitor? visitor = null)
    {
        List<IPropertyAdapter> props = [.. properties];
        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        CoreObjectPropertyEditorBuilder.CreateContexts(props, extensionProvider, context =>
        {
            tempItems.Add(context);
            if (visitor != null)
                context.Accept(visitor);
        });

        CoreObjectPropertyEditorBuilder.GroupByDisplayGroupName(tempItems);
        return tempItems;
    }

    private PropertyEditorExtension[] Extensions => extensionProvider.GetExtensions<PropertyEditorExtension>();
}
