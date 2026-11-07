// Adapted from FluentAvalonia's ColorPaletteItem:
// https://github.com/amwx/FluentAvalonia/blob/9e35813b3e3e649ad254ff6be4a35e52ac96d051/src/FluentAvalonia/UI/Controls/ColorPicker/ColorPaletteItem.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Utilities;

namespace Beutl.Controls.PropertyEditors;

/// <summary>
/// Defines the control used in a color palette to preview or select a color
/// </summary>
public partial class ColorPaletteItem : Control
{
    // The squares that show through a color that is not opaque.
    private const double CheckerSize = 4;
    private const double SelectionThickness = 2;
    // The room between the selection ring and the color inside it.
    private const double SelectionGap = 1;
    private static readonly IImmutableSolidColorBrush s_checkerLight = new ImmutableSolidColorBrush(0xFFFFFFFF);
    private static readonly IImmutableSolidColorBrush s_checkerDark = new ImmutableSolidColorBrush(0xFFCCCCCC);

    static ColorPaletteItem()
    {
        // The keyboard reaches the colors too: Tab or the arrow keys focus one, and Enter or Space picks it.
        FocusableProperty.OverrideDefaultValue<ColorPaletteItem>(true);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        CornerRadius cornerRadius = CornerRadius;

        if (IsSelected && SelectionBrush is { } selectionBrush)
        {
            Rect ring = rect.Deflate(SelectionThickness * 0.5);
            context.DrawRectangle(null, new Pen(selectionBrush, SelectionThickness), new RoundedRect(ring, cornerRadius));
            rect = rect.Deflate(SelectionThickness + SelectionGap);
        }

        // A pressed item draws no border.
        Thickness borderThickness = _isPressed ? default :
            IsPointerOver ? BorderThicknessPointerOver : BorderThickness;
        IBrush? borderBrush = _isPressed ? null :
            IsPointerOver ? BorderBrushPointerOver : BorderBrush;

        // A non-uniform border is drawn with the top thickness.
        double thickness = borderThickness.Top;
        IPen? pen = thickness > 0 ? new Pen(borderBrush, thickness) : null;

        if (!MathUtilities.IsZero(thickness))
            rect = rect.Deflate(thickness * 0.5);

        RoundedRect roundedRect = new(rect, cornerRadius);
        if (_color.A < 255)
        {
            DrawChecker(context, roundedRect);
        }

        context.DrawRectangle(_colorBrush, pen, roundedRect);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty ||
            change.Property == ColorProperty ||
            change.Property == BorderBrushProperty ||
            change.Property == BorderThicknessProperty ||
            change.Property == CornerRadiusProperty ||
            change.Property == IsSelectedProperty ||
            change.Property == SelectionBrushProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPressed = true;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPressed && e.InitialPressMouseButton == MouseButton.Left)
        {
            _isPressed = false;
            InvalidateVisual();

            // A press that leaves the item before it is released does not click it.
            if (new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            {
                e.Handled = true;
                RaiseEvent(new RoutedEventArgs(ClickEvent, this));
            }
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isPressed)
        {
            _isPressed = false;
            InvalidateVisual();
        }
    }

    private static void DrawChecker(DrawingContext context, RoundedRect area)
    {
        Rect rect = area.Rect;
        using (context.PushClip(area))
        {
            context.DrawRectangle(s_checkerLight, null, rect);
            for (int row = 0; row * CheckerSize < rect.Height; row++)
            {
                for (int column = row % 2; column * CheckerSize < rect.Width; column += 2)
                {
                    context.DrawRectangle(s_checkerDark, null, new Rect(
                        rect.X + column * CheckerSize, rect.Y + row * CheckerSize, CheckerSize, CheckerSize));
                }
            }
        }
    }

    private bool _isPressed;
    private SolidColorBrush? _colorBrush;
}
