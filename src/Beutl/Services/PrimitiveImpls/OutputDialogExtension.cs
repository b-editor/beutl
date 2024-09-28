using Avalonia.Controls;
using Beutl.Pages;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class OutputDialogExtension : ToolWindowExtension
{
    public static readonly OutputDialogExtension Instance = new();

    public override string Name => "OutputPage";

    public override string DisplayName => Strings.Output;

    public override bool ShowAsDialog => true;

    public override bool RequiresEditorContext => false;

    public override bool AllowMultiple => false;

    public override IToolWindowContext CreateContext(IEditorContext? editorContext)
    {
        return new OutputDialogViewModel();
    }

    public override Window CreateWindow(IEditorContext? editorContext)
    {
        return new OutputDialog();
    }
}
