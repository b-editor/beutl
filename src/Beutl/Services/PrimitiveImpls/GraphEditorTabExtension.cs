using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

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
        if (editorContext is EditViewModel)
        {
            control = new GraphEditorTab();
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
            context = new GraphEditorTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
