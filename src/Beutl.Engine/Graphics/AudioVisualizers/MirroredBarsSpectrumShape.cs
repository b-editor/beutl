using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.SpectrumShape_MirroredBars), ResourceType = typeof(GraphicsStrings))]
public sealed partial class MirroredBarsSpectrumShape : SpectrumShape
{
    public MirroredBarsSpectrumShape()
    {
        ScanProperties<MirroredBarsSpectrumShape>();
    }

    [Display(Name = nameof(GraphicsStrings.SpectrumShape_BarWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(0.5f, 10000f)]
    public IProperty<float> BarWidth { get; } = Property.CreateAnimatable(6f);

    public new partial class Resource
    {
        internal override void Render(
            ImmediateCanvas canvas,
            Rect bounds,
            ReadOnlySpan<float> normalizedBars,
            SolidColorBrush.Resource foregroundBrush,
            Color foregroundColor)
        {
            int barCount = normalizedBars.Length;
            if (barCount == 0) return;

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float centerY = (float)bounds.Y + height * 0.5f;
            float slotWidth = width / barCount;
            float barWidth = MathF.Max(0.5f, BarWidth);
            float offsetX = (slotWidth - barWidth) * 0.5f;

            for (int i = 0; i < barCount; i++)
            {
                float magnitude = normalizedBars[i];
                float halfHeight = MathF.Max(0.5f, magnitude * height * 0.5f);
                float x = (float)bounds.X + i * slotWidth + offsetX;
                float topY = centerY - halfHeight;
                float barHeight = halfHeight * 2f;
                canvas.DrawRectangle(new Rect(x, topY, barWidth, barHeight), foregroundBrush, null);
            }
        }
    }
}
