using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.SpectrumShape_Bar), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BarSpectrumShape : SpectrumShape
{
    public BarSpectrumShape()
    {
        ScanProperties<BarSpectrumShape>();
    }

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_Spacing), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 0.99f)]
    public IProperty<float> Spacing { get; } = Property.CreateAnimatable(0.1f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_CornerRadius), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CornerRadius> CornerRadius { get; } =
        Property.CreateAnimatable<CornerRadius>(new CornerRadius(0f));

    public new partial class Resource
    {
        private SKPaint? _paint;
        private Color _lastColor;
        private SKPath? _path;

        internal override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            SolidColorBrush.Resource foregroundBrush,
            Color foregroundColor)
        {
            int barCount = normalizedBars.Length;
            if (barCount == 0) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float slotWidth = width / barCount;
            float spacing = Math.Clamp(Spacing, 0f, 0.99f);
            float barWidth = MathF.Max(1f, slotWidth * (1f - spacing));
            float offsetX = (slotWidth - barWidth) * 0.5f;

            CornerRadius cr = CornerRadius;

            if (cr.IsEmpty)
            {
                for (int i = 0; i < barCount; i++)
                {
                    float magnitude = normalizedBars[i];
                    float barHeight = MathF.Max(1f, magnitude * height);
                    float x = (float)bounds.X + i * slotWidth + offsetX;
                    float y = (float)bounds.Y + height - barHeight;
                    canvas.DrawRectangle(new Rect(x, y, barWidth, barHeight), foregroundBrush, null);
                }
                return;
            }

            EnsurePaint(foregroundColor);
            _path ??= new SKPath();
            _path.Reset();

            for (int i = 0; i < barCount; i++)
            {
                float magnitude = normalizedBars[i];
                float barHeight = MathF.Max(1f, magnitude * height);
                float x = (float)bounds.X + i * slotWidth + offsetX;
                float y = (float)bounds.Y + height - barHeight;

                float maxRadius = MathF.Min(barWidth, barHeight) * 0.5f;
                float tl = MathF.Min(cr.TopLeft, maxRadius);
                float tr = MathF.Min(cr.TopRight, maxRadius);
                float br = MathF.Min(cr.BottomRight, maxRadius);
                float bl = MathF.Min(cr.BottomLeft, maxRadius);

                var rect = new SKRect(x, y, x + barWidth, y + barHeight);
                var radii = new SKPoint[4]
                {
                    new SKPoint(tl, tl),
                    new SKPoint(tr, tr),
                    new SKPoint(br, br),
                    new SKPoint(bl, bl),
                };
                using var roundRect = new SKRoundRect();
                roundRect.SetRectRadii(rect, radii);
                _path.AddRoundRect(roundRect);
            }

            canvas.Canvas.DrawPath(_path, _paint!);
        }

        private void EnsurePaint(Color color)
        {
            if (_paint == null)
            {
                _paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(color.R, color.G, color.B, color.A),
                };
                _lastColor = color;
            }
            else if (_lastColor != color)
            {
                _paint.Color = new SKColor(color.R, color.G, color.B, color.A);
                _lastColor = color;
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _paint?.Dispose();
                _path?.Dispose();
            }
            _paint = null;
            _path = null;
        }
    }
}
