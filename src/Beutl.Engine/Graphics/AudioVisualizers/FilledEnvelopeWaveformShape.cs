using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.WaveformShape_FilledEnvelope), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FilledEnvelopeWaveformShape : WaveformShape
{
    public FilledEnvelopeWaveformShape()
    {
        ScanProperties<FilledEnvelopeWaveformShape>();
    }

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_Smoothness), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> Smoothness { get; } = Property.CreateAnimatable(0f);

    // true なら max(|min|, |max|) の ±絶対値 を用いて完全上下対称の形状になる
    [Display(Name = nameof(GraphicsStrings.WaveformShape_Symmetric), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Symmetric { get; } = Property.Create(false);

    public new partial class Resource
    {
        private readonly CornerPathEffectCache _cornerEffect = new();
        private SKPath? _path;
        private SKPaint? _paint;

        protected internal override void Render(in WaveformRenderContext context)
        {
            ImmediateCanvas canvas = context.Canvas;
            Rect bounds = context.Bounds;
            ReadOnlySpan<float> mins = context.Mins;
            ReadOnlySpan<float> maxs = context.Maxs;
            float gain = context.Gain;
            Brush.Resource fill = context.Fill;

            int barCount = mins.Length;
            if (barCount < 2) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float halfHeight = height * 0.5f;
            float centerY = (float)bounds.Y + halfHeight;
            float slotWidth = width / barCount;
            float smoothness = Math.Clamp(Smoothness / 100f, 0f, 1f);
            float cornerRadius = smoothness * slotWidth * 2f;
            bool symmetric = Symmetric;

            _paint ??= new SKPaint();
            VisualizerPaint.ConfigureFill(_paint, canvas, bounds, fill);
            _paint.PathEffect = _cornerEffect.GetOrCreate(cornerRadius);

            _path ??= new SKPath();
            _path.Reset();

            // 上側 (max or abs) を左→右
            for (int i = 0; i < barCount; i++)
            {
                float v;
                if (symmetric)
                {
                    v = MathF.Max(MathF.Abs(mins[i]), MathF.Abs(maxs[i]));
                    v = Math.Clamp(v * gain, 0f, 1f);
                }
                else
                {
                    v = Math.Clamp(maxs[i] * gain, -1f, 1f);
                }
                float x = (float)bounds.X + i * slotWidth + slotWidth * 0.5f;
                float y = centerY - v * halfHeight;
                if (i == 0) _path.MoveTo(x, y);
                else _path.LineTo(x, y);
            }
            // 下側 (min or -abs) を右→左
            for (int i = barCount - 1; i >= 0; i--)
            {
                float v;
                if (symmetric)
                {
                    float peak = MathF.Max(MathF.Abs(mins[i]), MathF.Abs(maxs[i]));
                    v = -Math.Clamp(peak * gain, 0f, 1f);
                }
                else
                {
                    v = Math.Clamp(mins[i] * gain, -1f, 1f);
                }
                float x = (float)bounds.X + i * slotWidth + slotWidth * 0.5f;
                float y = centerY - v * halfHeight;
                _path.LineTo(x, y);
            }
            _path.Close();

            canvas.Canvas.DrawPath(_path, _paint);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _path?.Dispose();
                _paint?.Dispose();
                _cornerEffect.Dispose();
            }
            _path = null;
            _paint = null;
        }
    }
}
