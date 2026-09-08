using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class AiWorkspaceTabExtension : ToolTabExtension
{
    public static readonly AiWorkspaceTabExtension Instance = new();

    public override string Name => "AI";

    public override string DisplayName => Strings.Ai;

    public override string? Header => Strings.Ai;

    // Several AI tabs can be open at once so two pages can sit side by side. Each
    // tab keeps its own work, so a second one is a second workbench, not a mirror.
    public override bool CanMultiple => true;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 90;

    public override bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new AiWorkspaceView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            && lifetime.MainWindow?.DataContext is MainViewModel mainViewModel)
        {
            context = mainViewModel.CreateAiWorkspaceViewModel(editViewModel);
            return true;
        }

        context = null;
        return false;
    }
}
