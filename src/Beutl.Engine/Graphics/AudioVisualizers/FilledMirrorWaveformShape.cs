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
            => BarGeometry.DrawWaveformBars(
                in context,
                ref _paint,
                ref _builder,
                BarWidth,
                CornerRadius,
                static (in WaveformRenderContext c, int i, float centerY, float halfHeight) =>
                {
                    float magnitude = WaveformSampleMath.PeakMagnitude(c.Mins[i], c.Maxs[i], c.Gain);
                    float halfBarHeight = MathF.Max(0.5f, magnitude * halfHeight);
                    return (centerY - halfBarHeight, halfBarHeight * 2f);
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
