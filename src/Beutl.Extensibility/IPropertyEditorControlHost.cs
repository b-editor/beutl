using Avalonia.Controls;

namespace Beutl.Extensibility;

/// <summary>Optional host decoration for property editors, including nested object editors.</summary>
public interface IPropertyEditorControlHost
{
    Control WrapEditor(IPropertyEditorContext context, Control editor);
}
