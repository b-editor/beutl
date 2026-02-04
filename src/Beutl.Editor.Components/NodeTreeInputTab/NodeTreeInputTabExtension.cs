using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.NodeTreeInputTab.ViewModels;
using Beutl.Editor.Components.NodeTreeInputTab.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.NodeTreeInputTab;

[PrimitiveImpl]
public sealed class NodeTreeInputTabExtension : ToolTabExtension
{
    public static readonly NodeTreeInputTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Node Input";

    public override string DisplayName => "Node Input";

    public override string? Header => "Node Input";

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.PlugConnectedSettings };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new NodeTreeInputTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new NodeTreeInputTabViewModel(editorContext);
        return true;
    }
}
