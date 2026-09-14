using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.WaveformShape_Line), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LineWaveformShape : WaveformShape
{
    public LineWaveformShape()
    {
        ScanProperties<LineWaveformShape>();
    }

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_Thickness), ResourceType = typeof(GraphicsStrings))]
    [Range(0.5f, 50f)]
    public IProperty<float> Thickness { get; } = Property.CreateAnimatable(1.5f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_Smoothness), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> Smoothness { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.WaveformShape_Mirrored), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Mirrored { get; } = Property.Create(false);

    public new partial class Resource
    {
        private readonly CornerPathEffectCache _cornerEffect = new();
        private SKPathBuilder? _builder;
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
            float thickness = MathF.Max(0.5f, Thickness);
            float smoothness = Math.Clamp(Smoothness / 100f, 0f, 1f);
            float cornerRadius = smoothness * slotWidth * 2f;
            bool mirrored = Mirrored;

            _paint ??= new SKPaint();
            VisualizerPaint.ConfigureStroke(_paint, canvas, bounds, fill, thickness);
            _paint.PathEffect = _cornerEffect.GetOrCreate(cornerRadius);

            _builder ??= new SKPathBuilder();
            _builder.Reset();

            // 上側包絡線 (max)
            for (int i = 0; i < barCount; i++)
            {
                float max = Math.Clamp(maxs[i] * gain, -1f, 1f);
                float x = (float)bounds.X + i * slotWidth + slotWidth * 0.5f;
                float y = centerY - max * halfHeight;
                if (i == 0) _builder.MoveTo(x, y);
                else _builder.LineTo(x, y);
            }

            if (mirrored)
            {
                // 下側包絡線 (min) を独立したサブパスで追加
                for (int i = 0; i < barCount; i++)
                {
                    float min = Math.Clamp(mins[i] * gain, -1f, 1f);
                    float x = (float)bounds.X + i * slotWidth + slotWidth * 0.5f;
                    float y = centerY - min * halfHeight;
                    if (i == 0) _builder.MoveTo(x, y);
                    else _builder.LineTo(x, y);
                }
            }

            using SKPath path = _builder.Detach();
            canvas.Canvas.DrawPath(path, _paint);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _builder?.Dispose();
                _paint?.Dispose();
                _cornerEffect.Dispose();
            }
            _builder = null;
            _paint = null;
        }
    }
}
