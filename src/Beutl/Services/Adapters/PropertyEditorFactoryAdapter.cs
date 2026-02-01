using Beutl.Editor.Services;

namespace Beutl.Services.Adapters;

internal sealed class PropertyEditorFactoryAdapter : IPropertyEditorFactory
{
    public static readonly PropertyEditorFactoryAdapter Instance = new();

    public IPropertyEditorContext? CreateEditor(IPropertyAdapter property)
    {
        var (props, ext) = PropertyEditorService.MatchProperty([property]);
        if (props != null && ext != null && ext.TryCreateContext(props, out var ctx))
            return ctx;
        return null;
    }

    public (IPropertyAdapter[]? Properties, PropertyEditorExtension? Extension) MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        return PropertyEditorService.MatchProperty(properties);
    }
}
