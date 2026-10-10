using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed partial class TransitionTabExtension : ToolTabExtension
{
    public static readonly TransitionTabExtension Instance = new();

    public override string Name => "Transition";

    public override string DisplayName => GraphicsStrings.ClipTransition;

    // Opened from the timeline on a transition, not from the menu.
    public override string? Header => null;

    public override bool CanMultiple => true;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 90;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.SlideTransition };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new TransitionTabView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new TransitionTabViewModel(editViewModel);
            return true;
        }

        context = null;
        return false;
    }
}
