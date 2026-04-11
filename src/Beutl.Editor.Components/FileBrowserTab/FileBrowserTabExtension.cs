using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;

namespace Beutl.Editor.Components.FileBrowserTab;

[PrimitiveImpl]
public sealed class FileBrowserTabExtension : ToolTabExtension
{
    public static readonly FileBrowserTabExtension Instance = new();

    public override string Name => "FileBrowser";

    public override string DisplayName => Strings.FileBrowser;

    public override string? Header => Strings.FileBrowser;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Left;

    public override bool OpenByDefault => true;

    public override int DefaultOrder => 1;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new FileBrowserTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new FileBrowserTabViewModel(editorContext);
        return true;
    }
}
