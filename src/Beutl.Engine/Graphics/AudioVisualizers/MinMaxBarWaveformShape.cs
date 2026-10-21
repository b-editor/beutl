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

    // 0 にすると slotWidth から自動決定。
    [Display(Name = nameof(GraphicsStrings.SpectrumShape_BarWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 10000f)]
    public IExpressionProperty<float> BarWidth { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_CornerRadius), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<CornerRadius> CornerRadius { get; } =
        Property.CreateAnimatable<CornerRadius>(new CornerRadius(0f));

    public new partial class Resource
    {
        private SKPaint? _paint;
        private SKPathBuilder? _builder;

        protected internal override void Render(in WaveformRenderContext context)
            => BarGeometry.DrawWaveformBars(
                in context,
                ref _paint,
                ref _builder,
                BarWidth,
                CornerRadius,
                static (in WaveformRenderContext c, int i, float centerY, float halfHeight) =>
                {
                    float min = Math.Clamp(c.Mins[i] * c.Gain, -1f, 1f);
                    float max = Math.Clamp(c.Maxs[i] * c.Gain, -1f, 1f);
                    float topY = centerY - max * halfHeight;
                    float bottomY = centerY - min * halfHeight;
                    return (topY, MathF.Max(1f, bottomY - topY));
                });

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
