using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.SpectrumShape_Line), ResourceType = typeof(GraphicsStrings))]
public sealed partial class LineSpectrumShape : SpectrumShape
{
    public LineSpectrumShape()
    {
        ScanProperties<LineSpectrumShape>();
    }

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_Thickness), ResourceType = typeof(GraphicsStrings))]
    [Range(0.5f, 50f)]
    public IProperty<float> Thickness { get; } = Property.CreateAnimatable(2f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_Smoothness), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 100f)]
    public IProperty<float> Smoothness { get; } = Property.CreateAnimatable(0f);

    public new partial class Resource
    {
        private SKPath? _path;
        private SKPaint? _paint;
        private float _lastCornerRadius = -1f;
        private SKPathEffect? _cornerEffect;

        internal override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            Brush.Resource fill)
        {
            int barCount = normalizedBars.Length;
            if (barCount < 2) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float slotWidth = width / barCount;
            float thickness = MathF.Max(0.5f, Thickness);
            float smoothness = Math.Clamp(Smoothness / 100f, 0f, 1f);
            float cornerRadius = smoothness * slotWidth * 0.5f;

            _paint ??= new SKPaint();
            new BrushConstructor(bounds, fill, BlendMode.SrcOver).ConfigurePaint(_paint);
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeCap = SKStrokeCap.Round;
            _paint.StrokeJoin = SKStrokeJoin.Round;
            _paint.StrokeWidth = thickness;
            if (_lastCornerRadius != cornerRadius)
            {
                _cornerEffect?.Dispose();
                _cornerEffect = cornerRadius > 0.01f ? SKPathEffect.CreateCorner(cornerRadius) : null;
                _lastCornerRadius = cornerRadius;
            }
            _paint.PathEffect = _cornerEffect;

            _path ??= new SKPath();
            _path.Reset();

            for (int i = 0; i < barCount; i++)
            {
                float magnitude = normalizedBars[i];
                float x = (float)bounds.X + i * slotWidth + slotWidth * 0.5f;
                float y = (float)bounds.Y + height - MathF.Max(1f, magnitude * height);
                if (i == 0) _path.MoveTo(x, y);
                else _path.LineTo(x, y);
            }

            canvas.Canvas.DrawPath(_path, _paint);
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _path?.Dispose();
                _paint?.Dispose();
                _cornerEffect?.Dispose();
            }
            _path = null;
            _paint = null;
            _cornerEffect = null;
        }
    }
}
