using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views;

namespace Beutl.Editor.Components.LibraryTab;

[PrimitiveImpl]
public sealed class LibraryTabExtension : ToolTabExtension
{
    public static readonly LibraryTabExtension Instance = new();

    public override string Name => "Library";

    public override string DisplayName => Strings.Library;

    public override bool CanMultiple => false;

    public override string? Header => Strings.Library;

    public override DockAnchor DefaultAnchor => DockAnchor.Left;

    public override bool OpenByDefault => true;

    public override int DefaultOrder => 0;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new LibraryTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new LibraryTabViewModel(editorContext);
        return true;
    }
}
