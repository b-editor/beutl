using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.WaveformShape_Radial), ResourceType = typeof(GraphicsStrings))]
public sealed partial class RadialWaveformShape : WaveformShape
{
    public RadialWaveformShape()
    {
        ScanProperties<RadialWaveformShape>();
    }

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_InnerRadius), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 10000f)]
    public IProperty<float> InnerRadius { get; } = Property.CreateAnimatable(40f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_StartAngle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> StartAngle { get; } = Property.CreateAnimatable(-90f);

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_BarWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(0.5f, 100f)]
    public IProperty<float> BarWidth { get; } = Property.CreateAnimatable(4f);

    public new partial class Resource
    {
        internal override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> mins,
            ReadOnlySpan<float> maxs,
            float gain,
            Brush.Resource fill)
        {
            int barCount = mins.Length;
            if (barCount == 0) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float cx = (float)bounds.X + width * 0.5f;
            float cy = (float)bounds.Y + height * 0.5f;
            float outerRadius = MathF.Min(width, height) * 0.5f;
            float innerRadius = MathF.Min(InnerRadius, outerRadius - 1f);
            if (innerRadius < 0f) innerRadius = 0f;
            float maxOut = outerRadius - innerRadius;
            float maxIn = innerRadius;
            if (maxOut <= 0f && maxIn <= 0f) return;

            float barWidth = MathF.Max(0.5f, BarWidth);
            float startAngleDeg = StartAngle;
            float angleStep = 360f / barCount;
            float half = barWidth * 0.5f;

            for (int i = 0; i < barCount; i++)
            {
                float max = Math.Clamp(maxs[i] * gain, -1f, 1f);
                float min = Math.Clamp(mins[i] * gain, -1f, 1f);
                float outLen = MathF.Max(0f, max) * maxOut;
                float inLen = MathF.Max(0f, -min) * maxIn;
                if (outLen <= 0.5f && inLen <= 0.5f) continue;

                float angleDeg = startAngleDeg + angleStep * i;
                float angleRad = angleDeg * MathF.PI / 180f;
                float translateX = cx + innerRadius * MathF.Cos(angleRad);
                float translateY = cy + innerRadius * MathF.Sin(angleRad);

                Matrix transform = Matrix.CreateRotation(angleRad) * Matrix.CreateTranslation(translateX, translateY);
                using (canvas.PushTransform(transform))
                {
                    if (outLen > 0.5f)
                    {
                        canvas.DrawRectangle(new Rect(0f, -half, outLen, barWidth), fill, null);
                    }
                    if (inLen > 0.5f)
                    {
                        canvas.DrawRectangle(new Rect(-inLen, -half, inLen, barWidth), fill, null);
                    }
                }
            }
        }
    }
}
