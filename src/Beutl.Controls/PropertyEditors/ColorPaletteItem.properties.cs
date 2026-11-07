// Adapted from FluentAvalonia's ColorPaletteItem:
// https://github.com/amwx/FluentAvalonia/blob/9e35813b3e3e649ad254ff6be4a35e52ac96d051/src/FluentAvalonia/UI/Controls/ColorPicker/ColorPaletteItem.properties.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Beutl.Controls.PropertyEditors;

public partial class ColorPaletteItem
{
    /// <summary>
    /// Defines the <see cref="Color"/> property
    /// </summary>
    public static readonly DirectProperty<ColorPaletteItem, Color> ColorProperty =
        AvaloniaProperty.RegisterDirect<ColorPaletteItem, Color>(nameof(Color),
            x => x.Color, (x, v) => x.Color = v);

    /// <summary>
    /// Defines the <see cref="BorderBrush"/> property
    /// </summary>
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<ColorPaletteItem>();

    /// <summary>
    /// Defines the <see cref="BorderBrushPointerOver"/> property
    /// </summary>
    public static readonly StyledProperty<IBrush?> BorderBrushPointerOverProperty =
        AvaloniaProperty.Register<ColorPaletteItem, IBrush?>(nameof(BorderBrushPointerOver));

    /// <summary>
    /// Defines the <see cref="BorderThickness"/> property
    /// </summary>
    public static readonly StyledProperty<Thickness> BorderThicknessProperty =
        Border.BorderThicknessProperty.AddOwner<ColorPaletteItem>();

    /// <summary>
    /// Defines the <see cref="BorderThicknessPointerOver"/> property
    /// </summary>
    public static readonly StyledProperty<Thickness> BorderThicknessPointerOverProperty =
        AvaloniaProperty.Register<ColorPaletteItem, Thickness>(nameof(BorderThicknessPointerOver));

    /// <summary>
    /// Defines the <see cref="CornerRadius"/> property
    /// </summary>
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<ColorPaletteItem>();

    /// <summary>
    /// Defines the <see cref="IsSelected"/> property
    /// </summary>
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<ColorPaletteItem, bool>(nameof(IsSelected));

    /// <summary>
    /// Defines the <see cref="SelectionBrush"/> property
    /// </summary>
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<ColorPaletteItem, IBrush?>(nameof(SelectionBrush));

    /// <summary>
    /// Defines the <see cref="Click"/> event
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> ClickEvent =
        RoutedEvent.Register<ColorPaletteItem, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);

    /// <summary>
    /// Gets or sets the color this item should represent
    /// </summary>
    public Color Color
    {
        get => _color;
        set
        {
            if (SetAndRaise(ColorProperty, ref _color, value))
            {
                if (_colorBrush == null)
                    _colorBrush = new SolidColorBrush();

                _colorBrush.Color = _color;
            }
        }
    }

    /// <summary>
    /// Gets or sets the Border Brush this item should use when rendering
    /// </summary>
    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the Border Brush this item should use when rendering and the
    /// pointer is over the item
    /// </summary>
    public IBrush? BorderBrushPointerOver
    {
        get => GetValue(BorderBrushPointerOverProperty);
        set => SetValue(BorderBrushPointerOverProperty, value);
    }

    /// <summary>
    /// Gets or sets the Border Thickness this item should use when rendering
    /// </summary>
    public Thickness BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the Border Thickness this item should use when rendering and the
    /// pointer is over th item
    /// </summary>
    public Thickness BorderThicknessPointerOver
    {
        get => GetValue(BorderThicknessPointerOverProperty);
        set => SetValue(BorderThicknessPointerOverProperty, value);
    }

    /// <summary>
    /// Gets or sets the Corner Radius this item should use when rendering
    /// </summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the item is the color being edited, which draws a ring around it
    /// </summary>
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush of the ring drawn around a selected item
    /// </summary>
    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>
    /// Raised when the item is clicked with the left button
    /// </summary>
    public event EventHandler<RoutedEventArgs>? Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    private Color _color;
}
