using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.WaveformShape_MinMaxBar), ResourceType = typeof(GraphicsStrings))]
public sealed partial class MinMaxBarWaveformShape : WaveformShape
{
    public MinMaxBarWaveformShape()
    {
        ScanProperties<MinMaxBarWaveformShape>();
    }

    // 0 にすると slotWidth から自動決定（従来の WaveformStyle.MinMaxBar と同じ挙動）。
    [Display(Name = nameof(GraphicsStrings.SpectrumShape_BarWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 10000f)]
    public IProperty<float> BarWidth { get; } = Property.CreateAnimatable(0f);

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
            ReadOnlySpan<float> mins,
            ReadOnlySpan<float> maxs,
            float gain,
            Brush.Resource fill)
        {
            int barCount = mins.Length;
            if (barCount == 0) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float centerY = (float)bounds.Y + height * 0.5f;
            float halfHeight = height * 0.5f;
            float slotWidth = width / barCount;
            float requested = BarWidth;
            float barWidth = requested > 0f ? MathF.Max(0.5f, requested) : MathF.Max(1f, slotWidth - 0.5f);
            float offsetX = (slotWidth - barWidth) * 0.5f;

            CornerRadius cr = CornerRadius;
            bool round = !cr.IsEmpty;

            if (round)
            {
                _paint ??= new SKPaint();
                new BrushConstructor(bounds, fill, BlendMode.SrcOver).ConfigurePaint(_paint);
                _paint.Style = SKPaintStyle.Fill;
                _path ??= new SKPath();
                _path.Reset();
            }

            for (int i = 0; i < barCount; i++)
            {
                float min = Math.Clamp(mins[i] * gain, -1f, 1f);
                float max = Math.Clamp(maxs[i] * gain, -1f, 1f);
                float topY = centerY - max * halfHeight;
                float bottomY = centerY - min * halfHeight;
                float barHeight = MathF.Max(1f, bottomY - topY);
                float x = (float)bounds.X + i * slotWidth + offsetX;

                if (round)
                {
                    float maxRadius = MathF.Min(barWidth, barHeight) * 0.5f;
                    float tl = MathF.Min(cr.TopLeft, maxRadius);
                    float tr = MathF.Min(cr.TopRight, maxRadius);
                    float br = MathF.Min(cr.BottomRight, maxRadius);
                    float bl = MathF.Min(cr.BottomLeft, maxRadius);

                    var rect = new SKRect(x, topY, x + barWidth, topY + barHeight);
                    var radii = new SKPoint[4]
                    {
                        new SKPoint(tl, tl),
                        new SKPoint(tr, tr),
                        new SKPoint(br, br),
                        new SKPoint(bl, bl),
                    };
                    using var roundRect = new SKRoundRect();
                    roundRect.SetRectRadii(rect, radii);
                    _path!.AddRoundRect(roundRect);
                }
                else
                {
                    canvas.DrawRectangle(new Rect(x, topY, barWidth, barHeight), fill, null);
                }
            }

            if (round)
            {
                canvas.Canvas.DrawPath(_path!, _paint!);
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
