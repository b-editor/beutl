// https://github.com/amwx/FluentAvalonia/blob/9e35813b3e3e649ad254ff6be4a35e52ac96d051/src/FluentAvalonia/UI/Controls/ColorPicker/ColorPaletteItem.cs

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Beutl.Utilities;

namespace FluentAvalonia.UI.Controls;

/// <summary>
/// Defines the control used in the Color Palette of a <see cref="FAColorPicker"/> to
/// preview or select a color
/// </summary>
public partial class ColorPaletteItem : Control
{
    public ColorPaletteItem()
    {
        MinWidth = 23;
        MinHeight = 23;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Thickness borderThickness = _isPressed ? BorderThicknessPressed :
            IsPointerOver ? BorderThicknessPointerOver : BorderThickness;
        IBrush? borderBrush = _isPressed ? BorderBrushPressed :
            IsPointerOver ? BorderBrushPointerOver : BorderBrush;

        // A non-uniform border is drawn with the top thickness.
        double thickness = borderThickness.Top;
        IPen? pen = thickness > 0 ? new Pen(borderBrush, thickness) : null;

        var rect = new Rect(Bounds.Size);
        if (!MathUtilities.IsZero(thickness))
            rect = rect.Deflate(thickness * 0.5);

        CornerRadius cornerRadius = CornerRadius;
        context.DrawRectangle(_colorBrush, pen, new RoundedRect(rect, cornerRadius.TopLeft, cornerRadius.TopRight,
            cornerRadius.BottomRight, cornerRadius.BottomLeft));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty ||
            change.Property == ColorProperty ||
            change.Property == BorderBrushProperty ||
            change.Property == BorderThicknessProperty ||
            change.Property == CornerRadiusProperty)
        {
            InvalidateVisual();
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
        if (e.InitialPressMouseButton == MouseButton.Left &&
            e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            _isPressed = false;
            InvalidateVisual();

            var cp = this.FindAncestorOfType<FAColorPicker>();
            if (cp != null)
            {
                cp.Color = Color;
                e.Handled = true;
            }
        }
    }

    private bool _isPressed;
    private SolidColorBrush? _colorBrush;
}
