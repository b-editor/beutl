using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class HistoryTabExtension : ToolTabExtension
{
    public static readonly HistoryTabExtension Instance = new();

    public override string Name => "History";

    public override string DisplayName => Strings.History;

    public override string? Header => Strings.History;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 100;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new HistoryView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new HistoryViewModel(editViewModel);
            return true;
        }

        context = null;
        return false;
    }
}
