using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.Extensibility;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.LibraryTab;

[PrimitiveImpl]
public sealed class LibraryTabExtension : ToolTabExtension
{
    public static readonly LibraryTabExtension Instance = new();

    public override string Name => "Library";

    public override string DisplayName => Strings.Library;

    public override bool CanMultiple => false;

    public override string? Header => Strings.Library;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Library };
    }

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
