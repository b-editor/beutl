using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls;

public partial class BcTabItem
{
    public static readonly StyledProperty<Control> IconProperty =
        AvaloniaProperty.Register<BcTabItem, Control>(nameof(Icon));

    public static readonly StyledProperty<bool> IsClosableProperty =
        AvaloniaProperty.Register<BcTabItem, bool>(nameof(IsClosable), true);

    public static readonly DirectProperty<BcTabItem, bool> IsClosingProperty =
        AvaloniaProperty.RegisterDirect<BcTabItem, bool>(nameof(IsClosing), o => o.IsClosing);

    public static readonly StyledProperty<bool> CanBeDraggedProperty =
        AvaloniaProperty.Register<BcTabItem, bool>(nameof(CanBeDragged), true);

    private bool _isclosing = false;

    public Control Icon
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
