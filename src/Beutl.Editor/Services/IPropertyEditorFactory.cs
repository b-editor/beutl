using Beutl.Extensibility;

namespace Beutl.Editor.Services;

public interface IPropertyEditorFactory
{
    IPropertyEditorContext? CreateEditor(IPropertyAdapter property);

    // The editor of a property in a node of the node graph.
    IPropertyEditorContext? CreateNodeEditor(IPropertyAdapter property);

    // The editor of an item in a list editor.
    IPropertyEditorContext? CreateListItemEditor(IPropertyAdapter property);

    IReadOnlyList<IPropertyEditorContext?> CreatePropertyEditorContexts(
        IReadOnlyList<IPropertyAdapter> properties,
        IPropertyEditorContextVisitor? visitor = null);
}
