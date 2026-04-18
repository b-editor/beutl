using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.SpectrumShape_FilledArea), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FilledAreaSpectrumShape : SpectrumShape
{
    public FilledAreaSpectrumShape()
    {
        ScanProperties<FilledAreaSpectrumShape>();
    }

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_Smoothness), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 1f)]
    public IProperty<float> Smoothness { get; } = Property.CreateAnimatable(0f);

    public new partial class Resource
    {
        private SKPath? _path;
        private SKPaint? _paint;
        private Color _lastColor;
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
            float smoothness = Math.Clamp(Smoothness, 0f, 1f);
            float cornerRadius = smoothness * slotWidth * 0.5f;

            EnsurePaint(foregroundColor, cornerRadius);

            _path ??= new SKPath();
            _path.Reset();

            float left = (float)bounds.X;
            float right = (float)bounds.X + width;
            float baseY = (float)bounds.Y + height;

            _path.MoveTo(left, baseY);
            for (int i = 0; i < barCount; i++)
            {
                float magnitude = normalizedBars[i];
                float x = left + i * slotWidth + slotWidth * 0.5f;
                float y = baseY - MathF.Max(1f, magnitude * height);
                _path.LineTo(x, y);
            }
            _path.LineTo(right, baseY);
            _path.Close();

            canvas.Canvas.DrawPath(_path, _paint!);
        }

        private void EnsurePaint(Color color, float cornerRadius)
        {
            if (_paint == null)
            {
                _paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(color.R, color.G, color.B, color.A),
                };
                _lastColor = color;
            }
            else if (_lastColor != color)
            {
                _paint.Color = new SKColor(color.R, color.G, color.B, color.A);
                _lastColor = color;
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
