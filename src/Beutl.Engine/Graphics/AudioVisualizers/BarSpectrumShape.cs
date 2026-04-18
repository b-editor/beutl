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

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_BarWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(0.5f, 10000f)]
    public IProperty<float> BarWidth { get; } = Property.CreateAnimatable(6f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_CornerRadius), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CornerRadius> CornerRadius { get; } =
        Property.CreateAnimatable<CornerRadius>(new CornerRadius(0f));

    public new partial class Resource
    {
        private SKPaint? _paint;
        private SKPath? _path;

        internal override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            Brush.Resource fill)
        {
            int barCount = normalizedBars.Length;
            if (barCount == 0) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float slotWidth = width / barCount;
            float barWidth = MathF.Max(0.5f, BarWidth);
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
                    canvas.DrawRectangle(new Rect(x, y, barWidth, barHeight), fill, null);
                }
                return;
            }

            _paint ??= new SKPaint();
            new BrushConstructor(bounds, fill, BlendMode.SrcOver).ConfigurePaint(_paint);
            _paint.Style = SKPaintStyle.Fill;

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

            canvas.Canvas.DrawPath(_path, _paint);
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
