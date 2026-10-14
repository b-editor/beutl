using Avalonia;
using Avalonia.Controls.Primitives;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.TimelineTab.Views;

// The badge that names the timeline's tool mode; its look is in TimelineResources.axaml.
public sealed class TimelineToolModeBadge : TemplatedControl
{
    public static readonly StyledProperty<Icon> IconProperty =
        AvaloniaProperty.Register<TimelineToolModeBadge, Icon>(nameof(Icon));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TimelineToolModeBadge, string?>(nameof(Text));

    public Icon Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
