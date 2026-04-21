using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.AudioWaveform), ResourceType = typeof(GraphicsStrings))]
public sealed partial class AudioWaveformDrawable : AudioVisualizerDrawable
{
    public AudioWaveformDrawable()
    {
        ScanProperties<AudioWaveformDrawable>();
        Shape.CurrentValue = new MinMaxBarWaveformShape();

        // 編集頻度順に並べ替え: Source → 見た目(Shape/Fill) → 信号調整(Gain) → 時間/解像度(WindowSeconds/BarCount) → サイズ(Width/Height)
        MoveProperty(Source, 0);
        MoveProperty(Shape, 1);
        MoveProperty(Fill, 2);
        MoveProperty(Gain, 3);
        MoveProperty(WindowSeconds, 4);
        MoveProperty(BarCount, 5);
        MoveProperty(Width, 6);
        MoveProperty(Height, 7);
    }

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Shape), ResourceType = typeof(GraphicsStrings))]
    public IProperty<WaveformShape?> Shape { get; } = Property.Create<WaveformShape?>();

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
            WaveformShape.Resource? shape = Shape;
            if (shape is null) return;

            int barCount = Math.Max(1, BarCount);
            if (_minBuf.Length < barCount) _minBuf = new float[barCount];
            if (_maxBuf.Length < barCount) _maxBuf = new float[barCount];
            Span<float> minBuf = _minBuf.AsSpan(0, barCount);
            Span<float> maxBuf = _maxBuf.AsSpan(0, barCount);
            SoundSamplingHelper.DownsampleMinMax(CachedSampleSpan, minBuf, maxBuf);

            float gain = Math.Max(0f, Gain);
            shape.Render(canvas, bounds, minBuf, maxBuf, gain, Fill);
        }
    }
}
