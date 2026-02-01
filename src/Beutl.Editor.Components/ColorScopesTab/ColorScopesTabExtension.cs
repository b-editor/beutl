using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.Views;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.ColorScopesTab;

[PrimitiveImpl]
public sealed class ColorScopesTabExtension : ToolTabExtension
{
    public static readonly ColorScopesTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Color Scopes Tab";

    public override string DisplayName => Strings.ColorScopes;

    public override string Header => Strings.ColorScopes;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Microscope };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new ColorScopesTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new ColorScopesTabViewModel(editorContext);
        return true;
    }
}
