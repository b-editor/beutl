using Beutl.Api.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Services;

namespace Beutl.Services.Adapters;

internal sealed class PropertyEditorFactoryAdapter(ExtensionProvider extensionProvider) : IPropertyEditorFactory
{
    public IPropertyEditorContext? CreateEditor(IPropertyAdapter property)
    {
        var (props, ext) = CoreObjectPropertyEditorBuilder.MatchProperty([property], extensionProvider);
        return props != null && ext != null
            ? ExtensionGuard.Call(ext, () => ext.TryCreateContext(props, out IPropertyEditorContext? ctx) ? ctx : null)
            : null;
    }

    public (IPropertyAdapter[]? Properties, PropertyEditorExtension? Extension) MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        return CoreObjectPropertyEditorBuilder.MatchProperty(properties, extensionProvider);
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
