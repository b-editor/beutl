using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.ColorGradingTab.Views;
using Beutl.Extensibility;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.ColorGradingTab;

[PrimitiveImpl]
public sealed class ColorGradingTabExtension : ToolTabExtension
{
    public static readonly ColorGradingTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Color Grading Tab";

    public override string DisplayName => Strings.ColorGrading;

    public override string Header => Strings.ColorGrading;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.ColorBackground };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new ColorGradingTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new ColorGradingTabViewModel(editorContext);
        return true;
    }
}
