using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.SilenceSplitTab.ViewModels;
using Beutl.Editor.Components.SilenceSplitTab.Views;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.SilenceSplitTab;

public sealed class SilenceSplitTabExtension : ToolTabExtension
{
    public static readonly SilenceSplitTabExtension Instance = new();

    public override string Name => "Auto Split by Silence";
    public override string DisplayName => Strings.AutoSplitBySilence;
    public override string? Header => null;
    public override bool CanMultiple => false;
    public override DockAnchor DefaultAnchor => DockAnchor.Right;
    public override FAIconSource? GetIcon() => new FluentIconSource { Icon = Icon.Cut };

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new SilenceSplitTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext.GetService(typeof(Scene)) is Scene
            && editorContext.GetService(typeof(HistoryManager)) is HistoryManager
            && editorContext.GetService(typeof(IPreviewPlayer)) is IPreviewPlayer)
        {
            context = new SilenceSplitTabViewModel(editorContext);
            return true;
        }
        context = null;
        return false;
    }
}
