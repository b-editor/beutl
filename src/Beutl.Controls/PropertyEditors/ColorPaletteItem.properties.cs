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
    [GeneratedStyledProperty(AddOwnerFrom = typeof(Border))]
    public partial IBrush? BorderBrush { get; set; }

    /// <summary>
    /// Gets or sets the Border Brush this item should use when rendering and the
    /// pointer is over the item
    /// </summary>
    [GeneratedStyledProperty]
    public partial IBrush? BorderBrushPointerOver { get; set; }

    /// <summary>
    /// Gets or sets the Border Thickness this item should use when rendering
    /// </summary>
    [GeneratedStyledProperty(AddOwnerFrom = typeof(Border))]
    public partial Thickness BorderThickness { get; set; }

    /// <summary>
    /// Gets or sets the Border Thickness this item should use when rendering and the
    /// pointer is over th item
    /// </summary>
    [GeneratedStyledProperty]
    public partial Thickness BorderThicknessPointerOver { get; set; }

    /// <summary>
    /// Gets or sets the Corner Radius this item should use when rendering
    /// </summary>
    [GeneratedStyledProperty(AddOwnerFrom = typeof(Border))]
    public partial CornerRadius CornerRadius { get; set; }

    /// <summary>
    /// Gets or sets whether the item is the color being edited, which draws a ring around it
    /// </summary>
    [GeneratedStyledProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Gets or sets the brush of the ring drawn around a selected item
    /// </summary>
    [GeneratedStyledProperty]
    public partial IBrush? SelectionBrush { get; set; }

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
