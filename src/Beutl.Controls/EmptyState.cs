using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls;

/// <summary>
/// Tells that a view has nothing to show yet: an icon, a title and a description, with an action such as a
/// button as its content. Each part that is not set takes no space.
/// </summary>
public class EmptyState : ContentControl
{
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<EmptyState, object?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Description));

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
