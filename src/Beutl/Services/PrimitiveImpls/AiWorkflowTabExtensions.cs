using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Tools;

namespace Beutl.Services.PrimitiveImpls;

internal abstract class AiWorkflowTabExtension : ToolTabExtension
{
    public sealed override bool CanMultiple => false;

    public sealed override DockAnchor DefaultAnchor => DockAnchor.Right;

    public sealed override bool OpenByDefault => false;

    public sealed override bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = CreateContent();
            return true;
        }

        control = null;
        return false;
    }

    public sealed override bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            && lifetime.MainWindow?.DataContext is MainViewModel mainViewModel)
        {
            context = CreateContext(mainViewModel, editViewModel);
            return true;
        }

        context = null;
        return false;
    }

    protected abstract Control CreateContent();

    protected abstract IToolContext CreateContext(MainViewModel mainViewModel, EditViewModel editViewModel);
}

[PrimitiveImpl]
internal sealed class AiImageGenerationTabExtension : AiWorkflowTabExtension
{
    public static readonly AiImageGenerationTabExtension Instance = new();

    public override string Name => "AI Image Generation";

    public override string DisplayName => Strings.AiImageGeneration;

    public override string Header => Strings.AiImageGeneration;

    public override int DefaultOrder => 91;

    protected override Control CreateContent() => new AiImageGenerationView();

    protected override IToolContext CreateContext(MainViewModel mainViewModel, EditViewModel editViewModel)
        => mainViewModel.CreateAiImageGenerationToolViewModel(editViewModel);
}

[PrimitiveImpl]
internal sealed class AiImageEditTabExtension : AiWorkflowTabExtension
{
    public static readonly AiImageEditTabExtension Instance = new();

    public override string Name => "AI Image Edit";

    public override string DisplayName => Strings.AiImageEdit;

    public override string Header => Strings.AiImageEdit;

    public override int DefaultOrder => 92;

    protected override Control CreateContent() => new AiImageEditView();

    protected override IToolContext CreateContext(MainViewModel mainViewModel, EditViewModel editViewModel)
        => mainViewModel.CreateAiImageEditToolViewModel(editViewModel);
}

[PrimitiveImpl]
internal sealed class AiSubtitleTabExtension : AiWorkflowTabExtension
{
    public static readonly AiSubtitleTabExtension Instance = new();

    public override string Name => "AI Subtitles";

    public override string DisplayName => Strings.AiSubtitle;

    public override string Header => Strings.AiSubtitle;

    public override int DefaultOrder => 93;

    protected override Control CreateContent() => new AiSubtitleView();

    protected override IToolContext CreateContext(MainViewModel mainViewModel, EditViewModel editViewModel)
        => mainViewModel.CreateAiSubtitleToolViewModel(editViewModel);
}

[PrimitiveImpl]
internal sealed class AiVideoGenerationTabExtension : AiWorkflowTabExtension
{
    public static readonly AiVideoGenerationTabExtension Instance = new();

    public override string Name => "AI Video Generation";

    public override string DisplayName => Strings.AiVideoGeneration;

    public override string Header => Strings.AiVideoGeneration;

    public override int DefaultOrder => 94;

    protected override Control CreateContent() => new AiVideoGenerationView();

    protected override IToolContext CreateContext(MainViewModel mainViewModel, EditViewModel editViewModel)
        => mainViewModel.CreateAiVideoGenerationToolViewModel(editViewModel);
}
