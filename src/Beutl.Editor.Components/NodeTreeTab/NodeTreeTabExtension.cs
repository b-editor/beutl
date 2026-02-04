using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.NodeTreeTab.ViewModels;
using Beutl.Editor.Components.NodeTreeTab.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.NodeTreeTab;

[PrimitiveImpl]
public sealed class NodeTreeTabExtension : ToolTabExtension
{
    public static readonly NodeTreeTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "NodeTree";

    public override string DisplayName => "NodeTree";

    public override string? Header => "Node Tree";

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Flow };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new NodeTreeTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new NodeTreeTabViewModel(editorContext);
        return true;
    }
}
