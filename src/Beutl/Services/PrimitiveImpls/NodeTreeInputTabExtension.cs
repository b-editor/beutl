using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.ViewModels;
using Beutl.ViewModels.NodeTree;
using Beutl.Views.NodeTree;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

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
        if (editorContext is EditViewModel)
        {
            control = new NodeTreeInputTab();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new NodeTreeInputTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
