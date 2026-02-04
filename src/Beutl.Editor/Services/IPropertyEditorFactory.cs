using Beutl.Extensibility;

namespace Beutl.Editor.Services;

public interface IPropertyEditorFactory
{
    IPropertyEditorContext? CreateEditor(IPropertyAdapter property);

    (IPropertyAdapter[]? Properties, PropertyEditorExtension? Extension) MatchProperty(IReadOnlyList<IPropertyAdapter> properties);

    IReadOnlyList<IPropertyEditorContext?> CreatePropertyEditorContexts(
        IReadOnlyList<IPropertyAdapter> properties,
        IPropertyEditorContextVisitor? visitor = null);
}
