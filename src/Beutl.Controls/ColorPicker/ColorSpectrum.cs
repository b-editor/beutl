using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using FluentAvalonia.UI.Media;

namespace FluentAvalonia.UI.Controls;

/// <summary>
/// Defines the control for displaying the interactive ways of modifying a color within
/// a <see cref="FAColorPicker"/>
/// </summary>
/// <remarks>
/// This control handles displaying the regular Color Spectrum, the Color Wheel, and
/// the HSV Color Triangle
/// </remarks>
public partial class ColorSpectrum : ColorPickerComponent
{
    public ColorSpectrum()
    {
        MinWidth = 150;
        MinHeight = 150;
        MaxWidth = 500;
        MaxHeight = 500;

        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    static ColorSpectrum()
    {
        FocusableProperty.OverrideDefaultValue<ColorSpectrum>(true);
    }

    public override void Render(DrawingContext context)
    {
        if (Shape == ColorSpectrumShape.Triangle)
        {
            var minD = Math.Min(Bounds.Width, Bounds.Height) - WheelPadding;
            if (minD < 1)
                return;
            _lastWheelRect = new Rect(Bounds.Width / 2 - minD / 2, Bounds.Height / 2 - minD / 2,
                minD, minD);
        }

        if (_tempBitmap == null)
            CreateBitmap();
        if (_tempBitmap is not { } bitmap)
            return;

        Rect rect = new Rect(Bounds.Size);

        using (context.PushClip(new Rect(Bounds.Size)))
        {
            if (Shape == ColorSpectrumShape.Spectrum)
            {
                context.DrawImage(bitmap, new Rect(bitmap.Size), rect);

                RenderSelectorRects(context, rect.Width, rect.Height);

                if (_borderPen != null)
                    context.DrawRectangle(new Pen(Brushes.Gray), rect);
            }
            else if (Shape == ColorSpectrumShape.Wheel)
            {
                var minD = Math.Min(Bounds.Width, Bounds.Height) - WheelPadding;
                if (_lastWheelRect.Width != minD)
                {
                    _lastWheelRect = new Rect(Bounds.Width / 2 - minD / 2, Bounds.Height / 2 - minD / 2,
                        minD, minD);
                }

                // The wheel shows the current value, so only a value change redraws it
                if (_wheelBitmapValue != Color.Valuef)
                    CreateBitmap();

                context.DrawImage(bitmap, new Rect(bitmap.Size), _lastWheelRect);
            }
            else if (Shape == ColorSpectrumShape.Triangle)
            {
                if (!IsTriangleBitmapCurrent())
                {
                    CreateBitmap();
                }

                if (_tempBitmap is { } triangleBitmap)
                    context.DrawImage(triangleBitmap, new Rect(triangleBitmap.Size), _lastWheelRect);

                RenderTriangleSelector(context);
            }
        }

        if (Shape == ColorSpectrumShape.Wheel)
        {
            // Don't clip this to bounds so draw here
            RenderWheelSelector(context);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _lastHTR = HitTestPoint(pt.Position);
            if (_lastHTR != HitTestResult.None)
            {
                SetColorFromHitTestPosition(pt.Position, _lastHTR);
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        // Pointer move locks onto the hit test from pointer down
        // Really matters in Triangle mode, so you don't suddenly
        // transition between the wheel & the triangle
        // Otherwise, it still lets you drag outside
        if (_lastHTR != HitTestResult.None)
        {
            SetColorFromHitTestPosition(e.GetCurrentPoint(this).Position, _lastHTR);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var cPt = e.GetCurrentPoint(this);
        if (_lastHTR != HitTestResult.None && e.InitialPressMouseButton == MouseButton.Left &&
            cPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            // Ensure we've set the color correctly on pointer up, but only if we're still in a 
            // hit testable area
            var result = HitTestPoint(cPt.Position);
            if (result != HitTestResult.None)
                SetColorFromHitTestPosition(cPt.Position, result);

            _lastHTR = HitTestResult.None;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                HandleLeftRightKey(e);
                break;

            case Key.Up:
            case Key.Down:
                HandleUpDownKey(e);
                break;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BorderBrushProperty ||
            change.Property == BorderThicknessProperty)
        {
            RecreateBorderPen();
            InvalidateVisual();
        }
    }

    protected override void OnColorChanged(Color2 oldColor, Color2 newColor)
    {
        base.OnColorChanged(oldColor, newColor);

        if (Shape == ColorSpectrumShape.Spectrum)
        {
            switch (Component)
            {
                case ColorComponent.Hue:
                    if (newColor.Hue >= 0 && Hue != oldColor.Hue)
                        CreateBitmap();
                    break;
                case ColorComponent.Saturation:
                    if (oldColor.Saturation != newColor.Saturation)
                        CreateBitmap();
                    break;
                case ColorComponent.Value:
                    if (oldColor.Value != newColor.Value)
                        CreateBitmap();
                    break;
                case ColorComponent.Red:
                    if (oldColor.R != newColor.R)
                        CreateBitmap();
                    break;
                case ColorComponent.Green:
                    if (oldColor.G != newColor.G)
                        CreateBitmap();
                    break;
                case ColorComponent.Blue:
                    if (oldColor.B != newColor.B)
                        CreateBitmap();
                    break;
            }
        }

        // From WinUI (ColorSpectrum.cpp - SelectionEllipseShouldBeLight)
        // Alt would be to calculate HSL Lightness from HSV
        // Simpler, but may not work as nicely
        // var light = Color.Valuef * (1 - (Color.Saturationf / 2f));
        newColor.GetRGB(out byte R, out byte G, out byte B, out _);
        var rg = R <= 10 ? R / 3294.0 : Math.Pow(R / 269.0 + 0.0513, 2.4);
        var gg = G <= 10 ? G / 3294.0 : Math.Pow(G / 269.0 + 0.0513, 2.4);
        var bg = B <= 10 ? B / 3294.0 : Math.Pow(B / 269.0 + 0.0513, 2.4);
        _shouldSelectorBeDark = (0.2126 * rg + 0.7152 * gg + 0.0722 * bg) > 0.5;

        InvalidateVisual();
    }

    protected override void OnComponentChanged(ColorComponent newValue)
    {
        CreateBitmap();
        base.OnComponentChanged(newValue); // this will call invalidate            
    }

    private void OnShapeChanged(ColorSpectrumShape shape)
    {
        _tempBitmap?.Dispose();
        _tempBitmap = null;
        CreateBitmap();
        InvalidateVisual();
    }

    private void CreateBitmap()
    {
        // Triangle sizes the bitmap to the control at all times so we have to make sure we skip this
        // if the control has no size
        // Spectrum & wheel can proceed as normal since they use a fixed bitmap...
        if (Shape == ColorSpectrumShape.Triangle && _lastWheelRect == default)
            return;

        if (Shape == ColorSpectrumShape.Spectrum)
        {
            if (_tempBitmap == null || !(_tempBitmap is WriteableBitmap))
            {
                _tempBitmap?.Dispose();
                _tempBitmap = new WriteableBitmap(new PixelSize(500, 500), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            }

            // Component represents the third, non-displayed, color component
            switch (Component)
            {
                case ColorComponent.Hue:
                    DrawValueSaturationBitmap();
                    break;
                case ColorComponent.Saturation:
                    DrawValueHueBitmap();
                    break;
                case ColorComponent.Value:
                    DrawSaturationHueBitmap();
                    break;
                case ColorComponent.Red:
                    DrawBlueGreenBitmap();
                    break;
                case ColorComponent.Green:
                    DrawBlueRedBitmap();
                    break;
                case ColorComponent.Blue:
                    DrawGreenRedBitmap();
                    break;

                default:
                    DrawValueSaturationBitmap();
                    break;
            }
        }
        else if (Shape == ColorSpectrumShape.Wheel)
        {
            if (_tempBitmap == null || !(_tempBitmap is WriteableBitmap))
            {
                _tempBitmap?.Dispose();
                _tempBitmap = new WriteableBitmap(new PixelSize(_defBitmapSize, _defBitmapSize), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            }

            DrawWheelBitmap();
        }
        else if (Shape == ColorSpectrumShape.Triangle)
        {
            var size = GetTriangleBitmapSize();
            if (_tempBitmap is not WriteableBitmap { } bitmap || bitmap.PixelSize != size)
            {
                _tempBitmap?.Dispose();
                _tempBitmap = new WriteableBitmap(size, new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            }

            DrawTriangleWheelBitmap();
        }
    }

    private WriteableBitmap GetWriteableBitmap()
        => _tempBitmap as WriteableBitmap ?? throw new InvalidOperationException("The spectrum bitmap is unavailable.");

    private void DrawValueSaturationBitmap()
    {
        using (var lok = GetWriteableBitmap().Lock())
        {
            unsafe
            {
                var pixels = (uint*)(void*)lok.Address;
                var count = _defBitmapSize * _defBitmapSize;
                var hue = Hue;
                float size = _defBitmapSize;

                Parallel.For(0, _defBitmapSize, i =>
                {
                    int start = i * _defBitmapSize;
                    uint x = 0;
                    var sat = (1 - (i / (size - 1)));
                    for (int j = start; j < start + _defBitmapSize; j++)
                    {
                        var value = (x / (size - 1));
                        Color2.HSVToUInt(hue, sat, value, out uint num);
                        pixels[j] = num;
                        x++;
                    }
                });
            }
        }
    }

    private void DrawValueHueBitmap()
    {
        using (var lok = GetWriteableBitmap().Lock())
        {
            unsafe
            {
                var pixels = (uint*)(void*)lok.Address;
                var count = _defBitmapSize * _defBitmapSize;
                var sat = Color.Saturationf;
                float size = _defBitmapSize;

                Parallel.For(0, _defBitmapSize, i =>
                {
                    int start = i * _defBitmapSize;
                    uint x = 0;
                    var hue = 360 - ((i / size) * 360);

                    for (int j = start; j < start + _defBitmapSize; j++)
                    {
                        var value = x / size;
                        Color2.HSVToUInt(hue, sat, value, out uint num);
                        pixels[j] = num;
                        x++;
                    }
                });
            }
        }
    }

    private void DrawSaturationHueBitmap()
    {
        using (var lok = GetWriteableBitmap().Lock())
        {
            unsafe
            {
                var pixels = (uint*)(void*)lok.Address;
                var count = _defBitmapSize * _defBitmapSize;
                var value = Color.Valuef;
                float size = _defBitmapSize;

                Parallel.For(0, _defBitmapSize, i =>
                {
                    int start = i * _defBitmapSize;
                    uint x = 0;
                    var hue = 360 - ((i / size) * 360);

                    for (int j = start; j < start + _defBitmapSize; j++)
                    {
                        var sat = x / size;
                        Color2.HSVToUInt(hue, sat, value, out uint num);
                        pixels[j] = num;
                        x++;
                    }
                });
            }
        }
    }

    private void DrawBlueGreenBitmap()
    {
        using (var lok = GetWriteableBitmap().Lock())
        {
            unsafe
            {
                var pixels = (uint*)(void*)lok.Address;
                var count = _defBitmapSize * _defBitmapSize;
                var red = (uint)Color.R;
                float size = _defBitmapSize;

                Parallel.For(0, _defBitmapSize, i =>
                {
                    int start = i * _defBitmapSize;
                    uint x = 0;
                    var green = 255 - ((i / size) * 255);

                    for (int j = start; j < start + _defBitmapSize; j++)
                    {
                        var blue = (x / size) * 255;
                        pixels[j] = (uint)(0xFF << 24 | red << 16 | (uint)green << 8 | (uint)blue);
                        x++;
                    }
                });
            }
        }
    }

    private void DrawBlueRedBitmap()
    {
        using (var lok = GetWriteableBitmap().Lock())
        {
            unsafe
            {
                var pixels = (uint*)(void*)lok.Address;
                var count = _defBitmapSize * _defBitmapSize;
                var green = (uint)Color.G;
                float size = _defBitmapSize;

                Parallel.For(0, _defBitmapSize, i =>
                {
                    int start = i * _defBitmapSize;
                    uint x = 0;
                    var red = 255 - ((i / size) * 255);

                    for (int j = start; j < start + _defBitmapSize; j++)
                    {
                        var blue = (x / size) * 255;
                        pixels[j] = (uint)(0xFF << 24 | (uint)red << 16 | (uint)green << 8 | (uint)blue);
                        x++;
                    }
                });
            }
        }
    }

    private void DrawGreenRedBitmap()
    {
        using (var lok = GetWriteableBitmap().Lock())
        {
            unsafe
            {
                var pixels = (uint*)(void*)lok.Address;
                var count = _defBitmapSize * _defBitmapSize;
                var blue = (uint)Color.B;
                float size = _defBitmapSize;

                Parallel.For(0, _defBitmapSize, i =>
                {
                    int start = i * _defBitmapSize;
                    uint x = 0;
                    var red = 255 - ((i / size) * 255);

                    for (int j = start; j < start + _defBitmapSize; j++)
                    {
                        var green = (x / size) * 255;
                        pixels[j] = (uint)(0xFF << 24 | (uint)red << 16 | (uint)green << 8 | (uint)blue);
                        x++;
                    }
                });
            }
        }
    }

    private void DrawWheelBitmap()
    {
        var value = Color.Valuef;
        using (var lok = GetWriteableBitmap().Lock())
        {
            unsafe
            {
                var pixels = (byte*)lok.Address;
                var rowBytes = lok.RowBytes;
                var size = lok.Size.Width;
                // The wheel fills the bitmap, which is drawn into _lastWheelRect, so a point's
                // distance from the center over half the width is the saturation that
                // SetColorFromHitTestPosition picks there
                float radius = size / 2f;

                Parallel.For(0, size, i =>
                {
                    var row = (uint*)(pixels + i * rowBytes);
                    var dy = radius - (i + 0.5f);
                    for (int j = 0; j < size; j++)
                    {
                        var dx = j + 0.5f - radius;
                        var dist = MathF.Sqrt(dx * dx + dy * dy);
                        var coverage = float.Clamp(radius - dist + 0.5f, 0, 1);
                        row[j] = coverage > 0
                            ? HSVToPremultipliedUInt(AngleToHue(dx, dy), MathF.Min(dist / radius, 1), value, coverage)
                            : 0;
                    }
                });
            }
        }

        _wheelBitmapValue = value;
    }

    private void DrawTriangleWheelBitmap()
    {
        var hue = Color.Huef;
        using (var lok = GetWriteableBitmap().Lock())
        {
            unsafe
            {
                var pixels = (byte*)lok.Address;
                var rowBytes = lok.RowBytes;
                var width = lok.Size.Width;
                var height = lok.Size.Height;

                // Work in the control's units, like RenderTriangleSelector and HitTestPoint, so the
                // color drawn at a point is the color picked there
                var wheelSize = (float)_lastWheelRect.Width;
                var pixelSize = wheelSize / width;
                var center = wheelSize / 2f;
                var outerRadius = center - (float)WheelPadding / 2f;
                var innerRadius = outerRadius - TriangleWheelThickness * (wheelSize / 500);

                // Same frame as SetColorFromHitTestPosition: the hue corner points up, the white
                // corner sits bottom left and the black corner bottom right
                var angle = -MathF.PI / 2 + hue * MathF.PI / 180;
                var cos = MathF.Cos(angle);
                var sin = MathF.Sin(angle);
                var sqrt3 = MathF.Sqrt(3);

                Parallel.For(0, height, i =>
                {
                    var row = (uint*)(pixels + i * rowBytes);
                    var y = (i + 0.5f) * pixelSize - center;
                    for (int j = 0; j < width; j++)
                    {
                        var x = (j + 0.5f) * pixelSize - center;
                        var dist = MathF.Sqrt(x * x + y * y);

                        float b = 0, g = 0, r = 0, a = 0;

                        var normX = (x * cos - y * sin) / innerRadius;
                        var normY = (x * sin + y * cos) / innerRadius;
                        // Distance inside the nearest edge; the triangle's inradius is half its circumradius
                        var edge = MathF.Min(0.5f - normY,
                            MathF.Min(0.5f + (sqrt3 / 2) * normX + 0.5f * normY,
                                0.5f - (sqrt3 / 2) * normX + 0.5f * normY));
                        var triangleCoverage = float.Clamp(edge * innerRadius / pixelSize + 0.5f, 0, 1);
                        if (triangleCoverage > 0)
                        {
                            var denom = sqrt3 * -normX - normY + 2;
                            var val = float.Clamp(denom / 3, 0, 1);
                            var sat = denom > 0.0001f ? float.Clamp((1 - 2 * normY) / denom, 0, 1) : 0;
                            Color2.HSVToRGB(hue, sat, val, out r, out g, out b);
                            r *= triangleCoverage;
                            g *= triangleCoverage;
                            b *= triangleCoverage;
                            a = triangleCoverage;
                        }

                        var ringCoverage = float.Clamp((outerRadius - dist) / pixelSize + 0.5f, 0, 1)
                                           * float.Clamp((dist - innerRadius) / pixelSize + 0.5f, 0, 1);
                        if (ringCoverage > 0)
                        {
                            Color2.HSVToRGB(AngleToHue(x, -y), 1, 1, out var ringR, out var ringG, out var ringB);
                            r = ringR * ringCoverage + r * (1 - ringCoverage);
                            g = ringG * ringCoverage + g * (1 - ringCoverage);
                            b = ringB * ringCoverage + b * (1 - ringCoverage);
                            a = ringCoverage + a * (1 - ringCoverage);
                        }

                        row[j] = ToUInt(r, g, b, a);
                    }
                });
            }
        }

        _triangleBitmapHue = hue;
    }

    private PixelSize GetTriangleBitmapSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return new PixelSize(Math.Max(1, (int)(_lastWheelRect.Width * scaling)),
            Math.Max(1, (int)(_lastWheelRect.Height * scaling)));
    }

    private bool IsTriangleBitmapCurrent()
        => _tempBitmap is WriteableBitmap bitmap
           && bitmap.PixelSize == GetTriangleBitmapSize()
           && _triangleBitmapHue == Color.Huef;

    // Hue of the direction (x, y), with y pointing up and red along +x, in [0, 360)
    private static float AngleToHue(float x, float y)
    {
        var hue = MathF.Atan2(y, x) * 180 / MathF.PI;
        if (hue < 0)
            hue += 360;
        return hue >= 360 ? 0 : hue;
    }

    private static uint HSVToPremultipliedUInt(float hue, float sat, float val, float alpha)
    {
        Color2.HSVToRGB(hue, sat, val, out var r, out var g, out var b);
        return ToUInt(r * alpha, g * alpha, b * alpha, alpha);
    }

    // Packs premultiplied channels in [0, 1] as a Bgra8888 pixel
    private static uint ToUInt(float r, float g, float b, float a)
        => (uint)(a * 255 + 0.5f) << 24 | (uint)(r * 255 + 0.5f) << 16 | (uint)(g * 255 + 0.5f) << 8 | (uint)(b * 255 + 0.5f);

    private void RenderSelectorRects(DrawingContext context, double width, double height)
    {
        double x = 0;
        double y = 0;
        Color2 color = Color;
        switch (Component)
        {
            case ColorComponent.Hue:
                x = width * color.Valuef;
                y = height * (1 - color.Saturationf);
                break;

            case ColorComponent.Saturation:
                x = width * color.Valuef;
                y = height * (1 - (Hue / 360d));
                break;

            case ColorComponent.Value:
                x = width * color.Saturationf;
                y = height * (1 - (Hue / 360d));
                break;

            case ColorComponent.Red:
                x = width * color.Bf;
                y = height * (1 - color.Gf);
                break;

            case ColorComponent.Green:
                x = width * color.Bf;
                y = height * (1 - color.Rf);
                break;

            case ColorComponent.Blue:
                x = width * color.Gf;
                y = height * (1 - color.Rf);
                break;
        }

        context.DrawRectangle(_shouldSelectorBeDark ? BlackPen : WhitePen, new Rect(x - 5, y - 5, 10, 10), 5f);
    }

    private void RenderWheelSelector(DrawingContext context)
    {
        var hue = Color.Huef;
        var sat = Color.Saturationf;
        var radius = _lastWheelRect.Width / 2;

        var x = _lastWheelRect.Center.X + (radius * sat) * MathF.Cos(hue * MathF.PI / 180);
        var y = _lastWheelRect.Center.Y - (radius * sat) * MathF.Sin(hue * MathF.PI / 180);

        context.DrawRectangle(_shouldSelectorBeDark ? BlackPen : WhitePen, new Rect(x - 5, y - 5, 10, 10), 5f);
    }

    private void RenderTriangleSelector(DrawingContext context)
    {
        // Apply scale to TriangleWheelThickness
        var wheelThicc = TriangleWheelThickness * (_lastWheelRect.Width / 500);
        var rect = _lastWheelRect.Inflate(-(wheelThicc + WheelPadding / 2));
        var radius = (rect.Width / 2);
        Color.GetHSVf(out float h, out float s, out float v, out float _);
        h *= MathF.PI / 180;
        var hx = rect.Center.X + radius * MathF.Cos(h);
        var hy = rect.Center.Y - radius * MathF.Sin(h);

        // Draw line - uses hx,hy
        context.DrawLine(_shouldSelectorBeDark ? BlackPen : WhitePen, new Point(hx, hy),
            new Point(rect.Center.X + (radius + wheelThicc) * MathF.Cos(h),
            rect.Center.Y - (radius + wheelThicc) * MathF.Sin(h)));

        // Draw ellipse
        var x = (3 * s * v / 2) - 0.5;
        var y = ((Math.Sqrt(3) / 2) * s * v) - (v * Math.Sqrt(3)) + (Math.Sqrt(3) / 2);

        var pt = new Point(x * radius, y * radius) * Matrix.CreateRotation(-h) * Matrix.CreateTranslation(_lastWheelRect.Center);

        context.DrawRectangle(_shouldSelectorBeDark ? BlackPen : WhitePen, new Rect(pt.X - 4, pt.Y - 4, 8, 8), 8);
    }

    private HitTestResult HitTestPoint(Point pt)
    {
        if (Shape == ColorSpectrumShape.Spectrum)
        {
            return HitTestResult.Spectrum;
        }
        else if (Shape == ColorSpectrumShape.Wheel)
        {
            var dist = Math.Sqrt(Math.Pow(pt.X - _lastWheelRect.Center.X, 2) + Math.Pow(pt.Y - _lastWheelRect.Center.Y, 2));

            if (dist <= _lastWheelRect.Width / 2)
            {
                return HitTestResult.ColorWheel;
            }
        }
        else if (Shape == ColorSpectrumShape.Triangle)
        {
            var dist = Math.Sqrt(Math.Pow(pt.X - _lastWheelRect.Center.X, 2) + Math.Pow(pt.Y - _lastWheelRect.Center.Y, 2));
            var radius = _lastWheelRect.Width / 2;
            var wheelThicc = TriangleWheelThickness * (_lastWheelRect.Width / 500);
            var innerRadius = radius - wheelThicc - 2;

            if (dist >= innerRadius && dist <= radius)
            {
                return HitTestResult.TriangleWheel;
            }
            else
            {
                var h = Color.Huef * MathF.PI / 180;
                float third = MathF.PI * (2f / 3f);
                var hx = _lastWheelRect.Center.X + innerRadius * MathF.Cos(h);
                var hy = _lastWheelRect.Center.Y - innerRadius * MathF.Sin(h);

                var sx = _lastWheelRect.Center.X + innerRadius * MathF.Cos(h - third);
                var sy = _lastWheelRect.Center.Y - innerRadius * MathF.Sin(h - third);

                var vx = _lastWheelRect.Center.X + innerRadius * MathF.Cos(h + third);
                var vy = _lastWheelRect.Center.Y - innerRadius * MathF.Sin(h + third);

                if (PointInTriangle(pt, new Point(hx, hy), new Point(sx, sy), new Point(vx, vy)))
                {
                    return HitTestResult.Triangle;
                }
            }
        }

        return HitTestResult.None;
    }

    private void SetColorFromHitTestPosition(Point pt, HitTestResult htr)
    {
        switch (htr)
        {
            case HitTestResult.Spectrum:
                {
                    var sz = Bounds.Size;

                    var pX = double.Clamp(pt.X / sz.Width, 0, 1);
                    var pY = double.Clamp(pt.Y / sz.Height, 0, 1);

                    switch (Component)
                    {
                        case ColorComponent.Hue:
                            Color = Color2.FromHSVf(Hue, (float)(1 - pY), (float)pX);
                            break;
                        case ColorComponent.Saturation:
                            Color = Color2.FromHSVf((float)(1 - pY) * 359, Color.Saturationf, (float)pX);
                            break;
                        case ColorComponent.Value:
                            Color = Color2.FromHSVf((float)(1 - pY) * 359, (float)pX, Color.Valuef);
                            break;

                        case ColorComponent.Red:
                            Color = Color2.FromRGBf(Color.Rf, (float)(1 - pY), (float)pX, Color.Af);
                            break;
                        case ColorComponent.Green:
                            Color = Color2.FromRGBf((float)(1 - pY), Color.Gf, (float)(pX), Color.Af);
                            break;
                        case ColorComponent.Blue:
                            Color = Color2.FromRGBf((float)(1 - pY), (float)pX, Color.Bf, Color.Af);
                            break;
                    }
                }
                break;

            case HitTestResult.ColorWheel:
                {
                    var dp = pt - _lastWheelRect.Center;
                    var theta = Math.Atan2(-dp.Y, dp.X) * 180 / Math.PI;
                    if (theta < 0)
                        theta += 360;

                    var dist = double.Clamp(Math.Sqrt(dp.X * dp.X + dp.Y * dp.Y) / (_lastWheelRect.Width / 2), 0, 1);
                    Color = Color2.FromHSVf((float)double.Clamp(theta, 0, 360), (float)dist, Color.Valuef);
                }
                break;

            case HitTestResult.TriangleWheel:
                {
                    var newHue = 360 - (Math.Atan2(pt.Y - _lastWheelRect.Center.Y, pt.X - _lastWheelRect.Center.X) * 180 / Math.PI);
                    if (newHue < 0)
                        newHue += 360;
                    Color = Color.WithHuef((float)newHue);
                }
                break;

            case HitTestResult.Triangle:
                {
                    var radius = _lastWheelRect.Width / 2;
                    var wheelThicc = TriangleWheelThickness * (_lastWheelRect.Width / 500);
                    var innerRadius = radius - wheelThicc - 2;
                    var h = Color.Huef * MathF.PI / 180;

                    // Normalize points to Unit Circle & rotate accounting for hue & placing H=0 at top
                    // See below for more on this reasoning
                    // Then we know the coordinates of the triangle from the unit circle & we don't need
                    // to calculate them...
                    var norm = (pt - _lastWheelRect.Center) / innerRadius;
                    norm *= Matrix.CreateRotation(-Math.PI / 2 + h);

                    var newS = (1 - (2 * norm.Y)) / ((Math.Sqrt(3) * (norm.X * -1)) - norm.Y + 2);
                    var newV = (Math.Sqrt(3) * (norm.X * -1) - norm.Y + 2) / 3;

                    newV = MathF.Max(0, MathF.Min(1, (float)newV));
                    newS = MathF.Max(0, MathF.Min(1, (float)newS));
                    Color = Color2.FromHSVf(h * 180f / MathF.PI, (float)newS, (float)newV);
                }
                break;
        }
    }

    public static bool PointInTriangle(Point p, Point p0, Point p1, Point p2)
    {
        // https://stackoverflow.com/questions/2049582/how-to-determine-if-a-point-is-in-a-2d-triangle
        static double sign(Point p1, Point p2, Point p3)
        {
            return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        }

        double d1, d2, d3;
        bool has_neg, has_pos;

        d1 = sign(p, p0, p1);
        d2 = sign(p, p1, p2);
        d3 = sign(p, p2, p0);

        has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(has_neg && has_pos);
    }

    private void HandleUpDownKey(KeyEventArgs e)
    {
        bool inc = e.Key == Key.Up;
        var col = Color;
        if (Shape == ColorSpectrumShape.Spectrum)
        {
            // The "with[..]f" methods scale the values there so we don't 
            // need to check if we're in a valid range
            switch (Component)
            {
                case ColorComponent.Hue: //Adjust saturation
                    Color = col.WithSatf(col.Saturationf + (inc ? 0.01f : -0.01f));
                    break;
                case ColorComponent.Saturation: //Adjust hue
                case ColorComponent.Value:
                    Color = col.WithHuef(col.Huef + (inc ? 1f : -1f));
                    break;
                case ColorComponent.Red://Adjust green
                    Color = col.WithGreenf(col.Gf + (inc ? 0.01f : -0.01f));
                    break;
                case ColorComponent.Green: //Adjust red
                case ColorComponent.Blue:
                    Color = col.WithRedf(col.Rf + (inc ? 0.01f : -0.01f));
                    break;
            }
        }
        else if (Shape == ColorSpectrumShape.Wheel)
        {
            // WinUI/UWP, up/down adjusts the Saturation, so we'll follow that
            Color = col.WithSatf(col.Saturationf + (inc ? 0.01f : -0.01f));
        }
        else if (Shape == ColorSpectrumShape.Triangle)
        {
            // For simplicity, we'll map up/down to value
            // since v = 0 is the bottom corner of the triangle
            // If ctrl + up/down is used, we'll adjust the hue

            if ((KeyModifiers.Control & e.KeyModifiers) == KeyModifiers.Control)
            {
                var h = col.Huef + (inc ? 1f : -1f);
                if (h < 0)
                    h += 360;

                Color = col.WithHuef(h);
            }
            else
            {
                Color = col.WithValf(col.Valuef + (inc ? 0.01f : -0.01f));
            }
        }
        e.Handled = true;
    }

    private void HandleLeftRightKey(KeyEventArgs e)
    {
        bool inc = e.Key == Key.Right;
        var col = Color;
        if (Shape == ColorSpectrumShape.Spectrum)
        {
            // The "with[..]f" methods scale the values there so we don't 
            // need to check if we're in a valid range
            switch (Component)
            {
                case ColorComponent.Hue: //Adjust value
                case ColorComponent.Saturation:
                    Color = col.WithValf(col.Valuef + (inc ? 0.01f : -0.01f));
                    break;
                case ColorComponent.Value: //Adjust saturation
                    Color = col.WithSatf(col.Saturationf + (inc ? 0.01f : -0.01f));
                    break;

                case ColorComponent.Red: //Adjust blue
                case ColorComponent.Green:
                    Color = col.WithBluef(col.Bf + (inc ? 0.01f : -0.01f));
                    break;
                case ColorComponent.Blue: //Adjust green
                    Color = col.WithGreenf(col.Gf + (inc ? 0.01f : -0.01f));
                    break;
            }
        }
        else if (Shape == ColorSpectrumShape.Wheel)
        {
            // WinUI/UWP, left/right adjusts the Hue, so we'll follow that

            // To make sure the hue wraps around, we do the check here, otherwise it clamps
            var hue = col.Huef + (inc ? -1f : 1f);
            if (hue < 0)
                hue += 360f;
            if (hue >= 360)
                hue -= 360f;

            Color = col.WithHuef(hue);
        }
        else if (Shape == ColorSpectrumShape.Triangle)
        {
            // For simplicity, we'll map left/right to saturation
            // since s increases left to right (assuming H=0, right is top of triangle)
            // If ctrl + up/down is used, we'll adjust the hue
            if ((KeyModifiers.Control & e.KeyModifiers) == KeyModifiers.Control)
            {
                // Because of the direction of the wheel, we'll invert the key to 
                // increase the hue (left increases it)
                var h = col.Huef + (!inc ? 1f : -1f);
                if (h < 0)
                    h += 360;

                Color = col.WithHuef(h);
            }
            else
            {
                Color = col.WithSatf(col.Saturationf + (inc ? 0.01f : -0.01f));
            }
        }
        e.Handled = true;
    }

    private void RecreateBorderPen()
    {
        if (BorderBrush == null || BorderThickness == 0)
        {
            _borderPen = null;
            return;
        }

        _borderPen = new ImmutablePen(BorderBrush.ToImmutable(), BorderThickness);
    }

    private static readonly IPen BlackPen = new ImmutablePen(Brushes.Black, 2);
    private static readonly IPen WhitePen = new ImmutablePen(Brushes.White, 2);

    private bool _shouldSelectorBeDark;
    private HitTestResult _lastHTR;
    private readonly int _defBitmapSize = 500;
    private float _wheelBitmapValue;
    private float _triangleBitmapHue;

    private Bitmap? _tempBitmap;
    private Rect _lastWheelRect;
    private readonly double WheelPadding = 4;
    private readonly float TriangleWheelThickness = 50f;
    private IPen? _borderPen;

    private enum HitTestResult
    {
        None,
        Spectrum,
        ColorWheel,
        TriangleWheel,
        Triangle
    }
}
