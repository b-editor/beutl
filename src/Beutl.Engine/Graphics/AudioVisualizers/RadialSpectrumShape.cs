using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

public enum RadialSpectrumDirection
{
    Outward,
    Inward
}

[Display(Name = nameof(GraphicsStrings.SpectrumShape_Radial), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RadialSpectrumShape : SpectrumShape
{
    public RadialSpectrumShape()
    {
        ScanProperties<RadialSpectrumShape>();
    }

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_InnerRadius), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 10000f)]
    public IProperty<float> InnerRadius { get; } = Property.CreateAnimatable(40f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_StartAngle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> StartAngle { get; } = Property.CreateAnimatable(-90f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_BarWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(0.5f, 100f)]
    public IProperty<float> BarWidth { get; } = Property.CreateAnimatable(4f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_Direction), ResourceType = typeof(GraphicsStrings))]
    public IProperty<RadialSpectrumDirection> Direction { get; } =
        Property.Create(RadialSpectrumDirection.Outward);

    public new partial class Resource
    {
        internal override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            Brush.Resource fill)
        {
            int barCount = normalizedBars.Length;
            if (barCount == 0) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float cx = (float)bounds.X + width * 0.5f;
            float cy = (float)bounds.Y + height * 0.5f;
            float outerRadius = MathF.Min(width, height) * 0.5f;
            float innerRadius = MathF.Min(InnerRadius, outerRadius - 1f);
            if (innerRadius < 0f) innerRadius = 0f;
            float maxLen = outerRadius - innerRadius;
            if (maxLen <= 0f) return;

            float barWidth = MathF.Max(0.5f, BarWidth);
            float startAngleDeg = StartAngle;
            float angleStep = 360f / barCount;
            bool outward = Direction == RadialSpectrumDirection.Outward;

            for (int i = 0; i < barCount; i++)
            {
                float magnitude = normalizedBars[i];
                float len = MathF.Max(1f, magnitude * maxLen);

                float r1, r2;
                if (outward)
                {
                    r1 = innerRadius;
                    r2 = innerRadius + len;
                }
                else
                {
                    r1 = outerRadius - len;
                    r2 = outerRadius;
                }

                float angleDeg = startAngleDeg + angleStep * i;
                float angleRad = angleDeg * MathF.PI / 180f;

                float translateX = cx + r1 * MathF.Cos(angleRad);
                float translateY = cy + r1 * MathF.Sin(angleRad);

                Matrix transform = Matrix.CreateRotation(angleRad) * Matrix.CreateTranslation(translateX, translateY);
                using (canvas.PushTransform(transform))
                {
                    float half = barWidth * 0.5f;
                    canvas.DrawRectangle(new Rect(0f, -half, r2 - r1, barWidth), fill, null);
                }
            }
        }
    }
}
