using Avalonia.Controls;

namespace Beutl.Extensibility;

/// <summary>Optional host decoration for property editors, including nested object editors.</summary>
public interface IPropertyEditorControlHost
{
    /// <summary>Scopes the host to an editor's property before its child contexts are created.</summary>
    IPropertyEditorControlHost CreateChildHost(IPropertyAdapter property);

    Control WrapEditor(IPropertyEditorContext context, Control editor);
}
