using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.ViewModels;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ExtensionsDialogExtension : ToolWindowExtension
{
    public static readonly ExtensionsDialogExtension Instance = new();

    public override string Name => "ExtensionsDialog";

    public override string DisplayName => Strings.Extensions;

    public override bool ShowAsDialog => true;

    public override bool RequiresEditorContext => false;

    public override bool AllowMultiple => false;

    public override IToolWindowContext CreateContext(IEditorContext? editorContext)
    {
        var mainViewModel = Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow!.DataContext as MainViewModel
            : throw new InvalidOperationException("ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime");
        return new ExtensionsDialogViewModel(mainViewModel!._beutlClients);
    }

    public override Window CreateWindow(IEditorContext? editorContext)
    {
        return new Pages.ExtensionsDialog();
    }
}
