using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class OutputTabExtension : ToolTabExtension
{
    public static readonly OutputTabExtension Instance = new();

    public override string Name => "Output";

    public override string DisplayName => Strings.Output;

    public override string? Header => Strings.Output;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => true;

    public override int DefaultOrder => 1;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new OutputTab();
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
            context = new OutputTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
