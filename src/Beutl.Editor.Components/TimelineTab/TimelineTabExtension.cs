using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Services.PrimitiveImpls;

public sealed partial class TimelineTabExtension : ToolTabExtension
{
    public static readonly TimelineTabExtension Instance = new();

    public override string Name => "Timeline";

    public override string DisplayName => Strings.Timeline;

    public override bool CanMultiple => false;

    public override string? Header => Strings.Timeline;

    public override DockAnchor DefaultAnchor => DockAnchor.Bottom;

    public override bool OpenByDefault => true;

    public override int DefaultOrder => 0;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.GlanceHorizontal };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new TimelineTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new TimelineTabViewModel(editorContext);
        return true;
    }
}
