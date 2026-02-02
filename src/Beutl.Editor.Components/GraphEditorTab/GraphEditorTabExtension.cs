using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.GraphEditorTab;

[PrimitiveImpl]
public sealed class GraphEditorTabExtension : ToolTabExtension
{
    public static readonly GraphEditorTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Graph Editor Tab";

    public override string DisplayName => "Graph Editor Tab";

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.StarEmphasis };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new GraphEditorTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new GraphEditorTabViewModel(editorContext);
        return true;
    }
}
