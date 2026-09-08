using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class DockLayoutTabExtension : ToolTabExtension
{
    public static readonly DockLayoutTabExtension Instance = new();

    public override string Name => "Dock layout";

    public override string DisplayName => Strings.DockLayout;

    public override string? Header => Strings.DockLayout;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 110;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new DockLayoutView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new DockLayoutViewModel(editViewModel);
            return true;
        }

        context = null;
        return false;
    }
}
