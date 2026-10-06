using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.WaveformShape_FilledMirror), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FilledMirrorWaveformShape : WaveformShape
{
    public FilledMirrorWaveformShape()
    {
        ScanProperties<FilledMirrorWaveformShape>();
    }

    // 0 にすると slotWidth から自動決定
    [Display(Name = nameof(GraphicsStrings.SpectrumShape_BarWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 10000f)]
    public IProperty<float> BarWidth { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_CornerRadius), ResourceType = typeof(GraphicsStrings))]
    public IProperty<CornerRadius> CornerRadius { get; } =
        Property.CreateAnimatable<CornerRadius>(new CornerRadius(0f));

    public new partial class Resource
    {
        private SKPaint? _paint;
        private SKPathBuilder? _builder;

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
            float halfHeight = height * 0.5f;
            float centerY = (float)bounds.Y + halfHeight;
            float slotWidth = width / barCount;
            float barWidth = BarGeometry.ResolveWidth(BarWidth, slotWidth);
            float offsetX = (slotWidth - barWidth) * 0.5f;

            CornerRadius cr = CornerRadius;
            bool round = !cr.IsEmpty;

            if (round)
            {
                BarGeometry.BeginRoundedBars(ref _paint, ref _builder, canvas, bounds, fill);
            }

            for (int i = 0; i < barCount; i++)
            {
                float magnitude = WaveformSampleMath.PeakMagnitude(mins[i], maxs[i], gain);
                float halfBarHeight = MathF.Max(0.5f, magnitude * halfHeight);
                float topY = centerY - halfBarHeight;
                float barHeight = halfBarHeight * 2f;
                float x = (float)bounds.X + i * slotWidth + offsetX;

                if (round)
                {
                    BarGeometry.AddRoundedBar(_builder!, x, topY, barWidth, barHeight, cr);
                }
                else
                {
                    canvas.DrawRectangle(new Rect(x, topY, barWidth, barHeight), fill, null);
                }
            }

            if (round)
            {
                BarGeometry.DrawRoundedBars(canvas, _builder!, _paint!);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _paint?.Dispose();
                _builder?.Dispose();
            }
            _paint = null;
            _builder = null;
        }
    }
}
