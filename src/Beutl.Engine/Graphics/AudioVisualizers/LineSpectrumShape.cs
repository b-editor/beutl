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
        private Color _lastColor;
        private float _lastThickness = -1f;
        private float _lastCornerRadius = -1f;
        private SKPathEffect? _cornerEffect;

        internal override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            SolidColorBrush.Resource foregroundBrush,
            Color foregroundColor)
        {
            int barCount = normalizedBars.Length;
            if (barCount < 2) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float slotWidth = width / barCount;
            float thickness = MathF.Max(0.5f, Thickness);
            float smoothness = Math.Clamp(Smoothness / 100f, 0f, 1f);
            float cornerRadius = smoothness * slotWidth * 0.5f;

            EnsurePaint(foregroundColor, thickness, cornerRadius);

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

            canvas.Canvas.DrawPath(_path, _paint!);
        }

        private void EnsurePaint(Color color, float thickness, float cornerRadius)
        {
            if (_paint == null)
            {
                _paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    Color = new SKColor(color.R, color.G, color.B, color.A),
                    StrokeWidth = thickness,
                };
                _lastColor = color;
                _lastThickness = thickness;
            }
            else
            {
                if (_lastColor != color)
                {
                    _paint.Color = new SKColor(color.R, color.G, color.B, color.A);
                    _lastColor = color;
                }
                if (_lastThickness != thickness)
                {
                    _paint.StrokeWidth = thickness;
                    _lastThickness = thickness;
                }
            }

            if (_lastCornerRadius != cornerRadius)
            {
                _cornerEffect?.Dispose();
                _cornerEffect = cornerRadius > 0.01f ? SKPathEffect.CreateCorner(cornerRadius) : null;
                _paint.PathEffect = _cornerEffect;
                _lastCornerRadius = cornerRadius;
            }
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
