using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.ViewModels;

namespace Beutl.Services.Adapters;

internal sealed class PropertyEditorFactoryAdapter(ExtensionProvider extensionProvider) : IPropertyEditorFactory
{
    public IPropertyEditorContext? CreateEditor(IPropertyAdapter property)
    {
        var (props, ext) = PropertyEditorService.MatchProperty([property], extensionProvider);
        if (props != null && ext != null && ext.TryCreateContext(props, out var ctx))
            return ctx;
        return null;
    }

    public (IPropertyAdapter[]? Properties, PropertyEditorExtension? Extension) MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        return PropertyEditorService.MatchProperty(properties, extensionProvider);
    }

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
}
