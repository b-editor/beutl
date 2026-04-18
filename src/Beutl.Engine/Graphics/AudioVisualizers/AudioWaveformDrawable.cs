using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

public enum WaveformStyle
{
    MinMaxBar,
    Line,
    FilledMirror
}

[Display(Name = nameof(GraphicsStrings.AudioWaveform), ResourceType = typeof(GraphicsStrings))]
public sealed partial class AudioWaveformDrawable : AudioVisualizerDrawable
{
    public AudioWaveformDrawable()
    {
        ScanProperties<AudioWaveformDrawable>();
    }

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_WaveformStyle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<WaveformStyle> Style { get; } = Property.Create(WaveformStyle.MinMaxBar);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_BarCount), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 10000)]
    public IProperty<int> BarCount { get; } = Property.Create(256);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_WindowSeconds), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01f, 3600f)]
    public IProperty<float> WindowSeconds { get; } = Property.CreateAnimatable(4f);

    public new partial class Resource
    {
        private float[] _minBuf = [];
        private float[] _maxBuf = [];

        protected override (TimeSpan Start, TimeSpan Duration) ComputeSampleWindow(TimeSpan currentTime)
        {
            TimeSpan window = TimeSpan.FromSeconds(Math.Max(0.01, WindowSeconds));
            return (currentTime - TimeSpan.FromTicks(window.Ticks / 2), window);
        }

        protected override void RenderForeground(ImmediateCanvas canvas, Rect bounds)
        {
            if (CachedSampleLength == 0 || Fill is null) return;

            int barCount = Math.Max(1, BarCount);
            if (_minBuf.Length < barCount) _minBuf = new float[barCount];
            if (_maxBuf.Length < barCount) _maxBuf = new float[barCount];
            Span<float> minBuf = _minBuf.AsSpan(0, barCount);
            Span<float> maxBuf = _maxBuf.AsSpan(0, barCount);
            SoundSamplingHelper.DownsampleMinMax(CachedSampleSpan, minBuf, maxBuf);

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float centerY = height * 0.5f;
            float gain = Math.Max(0f, Gain);
            float slotWidth = width / barCount;
            float barWidth = Math.Max(1f, slotWidth - 0.5f);

            switch (Style)
            {
                case WaveformStyle.MinMaxBar:
                    for (int i = 0; i < barCount; i++)
                    {
                        float min = Math.Clamp(minBuf[i] * gain, -1f, 1f);
                        float max = Math.Clamp(maxBuf[i] * gain, -1f, 1f);
                        float topY = centerY - max * centerY;
                        float bottomY = centerY - min * centerY;
                        float barHeight = Math.Max(1f, bottomY - topY);
                        float x = i * slotWidth;
                        canvas.DrawRectangle(new Rect(x, topY, barWidth, barHeight), Fill, null);
                    }
                    break;

                case WaveformStyle.Line:
                    {
                        const float lineThickness = 1.5f;
                        float halfThick = lineThickness * 0.5f;
                        float? prevY = null;
                        for (int i = 0; i < barCount; i++)
                        {
                            float avg = (minBuf[i] + maxBuf[i]) * 0.5f;
                            float value = Math.Clamp(avg * gain, -1f, 1f);
                            float y = centerY - value * centerY;
                            float x = i * slotWidth;

                            canvas.DrawRectangle(new Rect(x, y - halfThick, barWidth, lineThickness), Fill, null);

                            if (prevY.HasValue)
                            {
                                float minY = Math.Min(prevY.Value, y);
                                float maxY = Math.Max(prevY.Value, y);
                                if (maxY - minY > lineThickness)
                                {
                                    canvas.DrawRectangle(new Rect(x - halfThick, minY, lineThickness, maxY - minY), Fill, null);
                                }
                            }
                            prevY = y;
                        }
                        break;
                    }

                case WaveformStyle.FilledMirror:
                    for (int i = 0; i < barCount; i++)
                    {
                        float abs = Math.Max(Math.Abs(minBuf[i]), Math.Abs(maxBuf[i]));
                        float magnitude = Math.Clamp(abs * gain, 0f, 1f);
                        float halfHeight = magnitude * centerY;
                        float topY = centerY - halfHeight;
                        float barHeight = Math.Max(1f, halfHeight * 2f);
                        float x = i * slotWidth;
                        canvas.DrawRectangle(new Rect(x, topY, barWidth, barHeight), Fill, null);
                    }
                    break;
            }
        }
    }
}
