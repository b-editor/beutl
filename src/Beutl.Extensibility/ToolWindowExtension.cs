using Avalonia.Controls;

namespace Beutl.Extensibility;

public abstract class ToolWindowExtension : Extension
{
    public abstract bool AllowMultiple { get; }

    public abstract bool ShowAsDialog { get; }

    public abstract bool RequiresEditorContext { get; }

    public abstract Window? CreateWindow(IEditorContext? editorContext);

    public abstract IToolWindowContext? CreateContext(IEditorContext? editorContext);
}

public interface IToolWindowContext : IDisposable
{
    ToolWindowExtension Extension { get; }
}
