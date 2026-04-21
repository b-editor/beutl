using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.WaveformShape_Block), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BlockWaveformShape : WaveformShape
{
    public BlockWaveformShape()
    {
        ScanProperties<BlockWaveformShape>();
    }

    [Display(Name = nameof(GraphicsStrings.WaveformShape_BlockCount), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 128)]
    public IProperty<int> BlockCount { get; } = Property.Create(16);

    [Display(Name = nameof(GraphicsStrings.WaveformShape_BlockGap), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 50f)]
    public IProperty<float> BlockGap { get; } = Property.CreateAnimatable(1f);

    // 0 にすると slotWidth から自動決定
    [Display(Name = nameof(GraphicsStrings.SpectrumShape_BarWidth), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 10000f)]
    public IProperty<float> BarWidth { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.WaveformShape_Mirrored), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> Mirrored { get; } = Property.Create(false);

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

            int blockCount = Math.Max(1, BlockCount);
            float blockGap = MathF.Max(0f, BlockGap);
            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float slotWidth = width / barCount;
            float requested = BarWidth;
            float barWidth = requested > 0f ? MathF.Max(0.5f, requested) : MathF.Max(1f, slotWidth - 0.5f);
            float offsetX = (slotWidth - barWidth) * 0.5f;
            bool mirrored = Mirrored;

            if (mirrored)
            {
                // 中央から上下対称に点灯。blockCount は片側ブロック数。
                float centerY = (float)bounds.Y + height * 0.5f;
                float halfHeight = height * 0.5f;
                float blockHeight = MathF.Max(0.5f, (halfHeight - blockGap * (blockCount - 1)) / blockCount);
                for (int i = 0; i < barCount; i++)
                {
                    float peak = MathF.Max(MathF.Abs(mins[i]), MathF.Abs(maxs[i]));
                    float magnitude = Math.Clamp(peak * gain, 0f, 1f);
                    int lit = (int)MathF.Round(magnitude * blockCount);
                    if (lit <= 0) continue;
                    float x = (float)bounds.X + i * slotWidth + offsetX;
                    for (int b = 0; b < lit; b++)
                    {
                        float topY = centerY - (b + 1) * blockHeight - b * blockGap;
                        canvas.DrawRectangle(new Rect(x, topY, barWidth, blockHeight), fill, null);
                        float botY = centerY + b * (blockHeight + blockGap);
                        canvas.DrawRectangle(new Rect(x, botY, barWidth, blockHeight), fill, null);
                    }
                }
            }
            else
            {
                // 下から上に点灯。
                float blockHeight = MathF.Max(0.5f, (height - blockGap * (blockCount - 1)) / blockCount);
                float baseY = (float)bounds.Y + height;
                for (int i = 0; i < barCount; i++)
                {
                    float peak = MathF.Max(MathF.Abs(mins[i]), MathF.Abs(maxs[i]));
                    float magnitude = Math.Clamp(peak * gain, 0f, 1f);
                    int lit = (int)MathF.Round(magnitude * blockCount);
                    if (lit <= 0) continue;
                    float x = (float)bounds.X + i * slotWidth + offsetX;
                    for (int b = 0; b < lit; b++)
                    {
                        float topY = baseY - (b + 1) * blockHeight - b * blockGap;
                        canvas.DrawRectangle(new Rect(x, topY, barWidth, blockHeight), fill, null);
                    }
                }
            }
        }
    }
}
