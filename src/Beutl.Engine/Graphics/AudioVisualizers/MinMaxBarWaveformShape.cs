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

        protected internal override void Render(in WaveformRenderContext context)
        {
            ImmediateCanvas canvas = context.Canvas;
            Rect bounds = context.Bounds;
            ReadOnlySpan<float> mins = context.Mins;
            ReadOnlySpan<float> maxs = context.Maxs;
            float gain = context.Gain;
            Brush.Resource fill = context.Fill;

            int barCount = mins.Length;
            if (barCount == 0) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float centerY = (float)bounds.Y + height * 0.5f;
            float halfHeight = height * 0.5f;
            float slotWidth = width / barCount;
            float barWidth = BarGeometry.ResolveWidth(BarWidth, slotWidth);
            float offsetX = (slotWidth - barWidth) * 0.5f;

            CornerRadius cr = CornerRadius;
            bool round = !cr.IsEmpty;

            if (round)
            {
                _paint ??= new SKPaint();
                VisualizerPaint.ConfigureFill(_paint, canvas, bounds, fill);
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
                    BarGeometry.AddRoundedBar(_path!, x, topY, barWidth, barHeight, cr);
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
