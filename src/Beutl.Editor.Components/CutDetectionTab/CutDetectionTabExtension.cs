using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.CutDetectionTab.ViewModels;
using Beutl.Editor.Components.CutDetectionTab.Views;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.CutDetectionTab;

public sealed class CutDetectionTabExtension : ToolTabExtension
{
    public static readonly CutDetectionTabExtension Instance = new();

    public override string Name => "Detect Cuts";
    public override string DisplayName => Strings.DetectCuts;
    public override string? Header => null;
    public override bool CanMultiple => false;
    public override DockAnchor DefaultAnchor => DockAnchor.Right;
    public override FAIconSource? GetIcon() => new FluentIconSource { Icon = Icon.Cut };

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new CutDetectionTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext.GetService(typeof(Scene)) is Scene
            && editorContext.GetService(typeof(HistoryManager)) is HistoryManager)
        {
            context = new CutDetectionTabViewModel(editorContext);
            return true;
        }

        context = null;
        return false;
    }
}
