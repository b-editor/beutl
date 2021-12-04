using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BEditorNext.Controls;

public partial class DraggableTabView
{
    public static readonly DirectProperty<DraggableTabView, object> FallBackContentProperty =
        AvaloniaProperty.RegisterDirect<DraggableTabView, object>
            (nameof(FallBackContent),
            o => o.FallBackContent,
            (o, v) => o.FallBackContent = v);

    public static readonly StyledProperty<bool> AdderButtonIsVisibleProperty =
        AvaloniaProperty.Register<DraggableTabView, bool>(nameof(AdderButtonIsVisible), true);

    public static readonly StyledProperty<double> MaxWidthOfItemsPresenterProperty =
        AvaloniaProperty.Register<DraggableTabView, double>(nameof(MaxWidthOfItemsPresenter), double.PositiveInfinity);

    public static readonly StyledProperty<IBrush> SecondaryBackgroundProperty =
        AvaloniaProperty.Register<DraggableTabView, IBrush>(nameof(SecondaryBackground));

    public static readonly StyledProperty<Thickness> ItemsMarginProperty =
        AvaloniaProperty.Register<DraggableTabView, Thickness>(nameof(ItemsMargin));

    public static readonly StyledProperty<bool> TransitionIsEnabledProperty =
        AvaloniaProperty.Register<DraggableTabView, bool>(nameof(TransitionIsEnabled));

    public static readonly DirectProperty<DraggableTabView, double> WidthRemainingSpaceProperty =
        AvaloniaProperty.RegisterDirect<DraggableTabView, double>(
            nameof(WidthRemainingSpace),
            o => o.WidthRemainingSpace);

    public static readonly DirectProperty<DraggableTabView, double> HeightRemainingSpaceProperty =
        AvaloniaProperty.RegisterDirect<DraggableTabView, double>(
            nameof(HeightRemainingSpace),
            o => o.HeightRemainingSpace);

    public static readonly StyledProperty<bool> ReorderableTabsProperty =
        AvaloniaProperty.Register<DraggableTabView, bool>(nameof(ReorderableTabs), true);

    public static readonly StyledProperty<bool> ImmediateDragProperty =
        AvaloniaProperty.Register<DraggableTabView, bool>(nameof(ImmediateDrag), true);

    private object _fallbackcontent = new TextBlock
    {
        Text = "Nothing here",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 16
    };

    private double _widthremainingspace;

    private double _heightremainingspace;

    public object FallBackContent
    {
        get => _fallbackcontent;
        set => SetAndRaise(FallBackContentProperty, ref _fallbackcontent, value);
    }

    public bool AdderButtonIsVisible
    {
        get => GetValue(AdderButtonIsVisibleProperty);
        set => SetValue(AdderButtonIsVisibleProperty, value);
    }

    public double MaxWidthOfItemsPresenter
    {
        get => GetValue(MaxWidthOfItemsPresenterProperty);
        set => SetValue(MaxWidthOfItemsPresenterProperty, value);
    }

    public IBrush SecondaryBackground
    {
        get => GetValue(SecondaryBackgroundProperty);
        set => SetValue(SecondaryBackgroundProperty, value);
    }

    public Thickness ItemsMargin
    {
        get => GetValue(ItemsMarginProperty);
        set => SetValue(ItemsMarginProperty, value);
    }

    public bool TransitionIsEnabled
    {
        get => GetValue(TransitionIsEnabledProperty);
        set => SetValue(TransitionIsEnabledProperty, value);
    }

    public double WidthRemainingSpace
    {
        get => _widthremainingspace;
        private set => SetAndRaise(WidthRemainingSpaceProperty, ref _widthremainingspace, value);
    }

    public double HeightRemainingSpace
    {
        get => _heightremainingspace;
        private set => SetAndRaise(HeightRemainingSpaceProperty, ref _heightremainingspace, value);
    }

    public bool ReorderableTabs
    {
        get => GetValue(ReorderableTabsProperty);
        set => SetValue(ReorderableTabsProperty, value);
    }

    public bool ImmediateDrag
    {
        get => GetValue(ImmediateDragProperty);
        set => SetValue(ImmediateDragProperty, value);
    }
}
