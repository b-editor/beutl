using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.ElementPropertyTab;

[PrimitiveImpl]
public sealed class ElementPropertyTabExtension : ToolTabExtension
{
    public static readonly ElementPropertyTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Element Property";

    public override string DisplayName => "Element Property";

    public override string? Header => Strings.ElementProperty;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Wrench };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new ElementPropertyTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new ElementPropertyTabViewModel(editorContext);
        return true;
    }
}
