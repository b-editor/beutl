using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class AiJobCenterTabExtension : ToolTabExtension
{
    public static readonly AiJobCenterTabExtension Instance = new();

    public override string Name => "AI Job Center";

    public override string DisplayName => Strings.AiJobCenter;

    public override string? Header => Strings.AiJobCenter;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 90;

    public override bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new AiJobCenterView();
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
            context = mainViewModel.CreateAiJobCenterViewModel(editViewModel);
            return true;
        }

        context = null;
        return false;
    }
}
