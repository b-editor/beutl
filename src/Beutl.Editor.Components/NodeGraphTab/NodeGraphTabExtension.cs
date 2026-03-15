using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.NodeGraphTab;

[PrimitiveImpl]
public sealed class NodeGraphTabExtension : ToolTabExtension
{
    public static readonly NodeGraphTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "NodeGraphTab";

    public override string DisplayName => "NodeGraph";

    public override string? Header => "GraphNode Tree";

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Flow };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new NodeGraphTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new NodeGraphTabViewModel(editorContext);
        return true;
    }
}
