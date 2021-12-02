using Avalonia;
using Avalonia.Controls;

namespace BEditorNext.Controls;

public partial class DraggableTabItem
{
    public static readonly StyledProperty<IconElement> IconProperty =
        AvaloniaProperty.Register<DraggableTabItem, IconElement>(nameof(Icon));

    public static readonly StyledProperty<bool> IsClosableProperty =
        AvaloniaProperty.Register<DraggableTabItem, bool>(nameof(IsClosable), true);

    public static readonly DirectProperty<DraggableTabItem, bool> IsClosingProperty =
        AvaloniaProperty.RegisterDirect<DraggableTabItem, bool>(nameof(IsClosing), o => o.IsClosing);

    public static readonly StyledProperty<bool> CanBeDraggedProperty =
        AvaloniaProperty.Register<DraggableTabItem, bool>(nameof(CanBeDragged), true);

    private bool _isclosing = false;

    public IconElement Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsClosable
    {
        get => GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    public bool IsClosing
    {
        get => _isclosing;
        private set => SetAndRaise(IsClosingProperty, ref _isclosing, value);
    }

    public bool CanBeDragged
    {
        get => GetValue(CanBeDraggedProperty);
        set => SetValue(CanBeDraggedProperty, value);
    }
}
