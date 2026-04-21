using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

public enum DotsWaveformMode
{
    MinMax,
    Center,
}

[Display(Name = nameof(GraphicsStrings.WaveformShape_Dots), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DotsWaveformShape : WaveformShape
{
    public DotsWaveformShape()
    {
        ScanProperties<DotsWaveformShape>();
    }

    [Display(Name = nameof(GraphicsStrings.WaveformShape_DotRadius), ResourceType = typeof(GraphicsStrings))]
    [Range(0.5f, 100f)]
    public IProperty<float> DotRadius { get; } = Property.CreateAnimatable(2f);

    [Display(Name = nameof(GraphicsStrings.WaveformShape_DotMode), ResourceType = typeof(GraphicsStrings))]
    public IProperty<DotsWaveformMode> Mode { get; } = Property.Create(DotsWaveformMode.MinMax);

    public new partial class Resource
    {
        private SKPath? _path;
        private SKPaint? _paint;

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
            float halfHeight = height * 0.5f;
            float centerY = (float)bounds.Y + halfHeight;
            float slotWidth = width / barCount;
            float dotRadius = MathF.Max(0.5f, DotRadius);
            bool minmax = Mode == DotsWaveformMode.MinMax;

            _paint ??= new SKPaint();
            new BrushConstructor(bounds, fill, BlendMode.SrcOver).ConfigurePaint(_paint);
            _paint.Style = SKPaintStyle.Fill;
            _paint.IsAntialias = true;
            _path ??= new SKPath();
            _path.Reset();

            for (int i = 0; i < barCount; i++)
            {
                float cx = (float)bounds.X + i * slotWidth + slotWidth * 0.5f;
                if (minmax)
                {
                    float max = Math.Clamp(maxs[i] * gain, -1f, 1f);
                    float min = Math.Clamp(mins[i] * gain, -1f, 1f);
                    _path.AddCircle(cx, centerY - max * halfHeight, dotRadius);
                    _path.AddCircle(cx, centerY - min * halfHeight, dotRadius);
                }
                else
                {
                    float center = (mins[i] + maxs[i]) * 0.5f;
                    float v = Math.Clamp(center * gain, -1f, 1f);
                    _path.AddCircle(cx, centerY - v * halfHeight, dotRadius);
                }
            }

            canvas.Canvas.DrawPath(_path, _paint);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _path?.Dispose();
                _paint?.Dispose();
            }
            _path = null;
            _paint = null;
        }
    }
}
