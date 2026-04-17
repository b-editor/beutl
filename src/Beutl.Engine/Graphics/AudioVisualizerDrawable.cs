using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics;

public enum AudioVisualizerMode
{
    Waveform,
    Spectrum,
    Spectrogram
}

public enum WaveformStyle
{
    MinMaxBar,
    Line,
    FilledMirror
}

[Display(Name = nameof(GraphicsStrings.AudioVisualizer), ResourceType = typeof(GraphicsStrings))]
public sealed partial class AudioVisualizerDrawable : Drawable
{
    public AudioVisualizerDrawable()
    {
        ScanProperties<AudioVisualizerDrawable>();
    }

    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<Sound?> Source { get; } = Property.Create<Sound?>();

    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(1, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(640f);

    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(1, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(120f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_DisplayMode), ResourceType = typeof(GraphicsStrings))]
    public IProperty<AudioVisualizerMode> DisplayMode { get; } = Property.Create(AudioVisualizerMode.Waveform);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_ForegroundColor), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> ForegroundColor { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_BackgroundColor), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> BackgroundColor { get; } = Property.CreateAnimatable(Colors.Transparent);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_WaveformStyle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<WaveformStyle> WaveformStyle { get; } = Property.Create(Graphics.WaveformStyle.MinMaxBar);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_BarCount), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 10000)]
    public IProperty<int> BarCount { get; } = Property.Create(256);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_WindowSeconds), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01f, 3600f)]
    public IProperty<float> WindowSeconds { get; } = Property.CreateAnimatable(4f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FftSize), ResourceType = typeof(GraphicsStrings))]
    [Range(64, 16384)]
    public IProperty<int> FftSize { get; } = Property.Create(1024);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_LogarithmicFrequency), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> LogarithmicFrequency { get; } = Property.Create(true);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Gain), ResourceType = typeof(GraphicsStrings))]
    [Range(0.01f, 1000f)]
    public IProperty<float> Gain { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FloorDb), ResourceType = typeof(GraphicsStrings))]
    [Range(-200f, 0f)]
    public IProperty<float> FloorDb { get; } = Property.CreateAnimatable(-80f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_TimeColumns), ResourceType = typeof(GraphicsStrings))]
    [Range(2, 2048)]
    public IProperty<int> TimeColumns { get; } = Property.Create(256);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        return new Size(MathF.Max(1f, r.Width), MathF.Max(1f, r.Height));
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        r.Render(context);
    }

    public partial class Resource
    {
        private const int IntensityLevels = 16;
        private const int DefaultComposerSampleRate = 44100;

        private SolidColorBrush.Resource? _foregroundBrushResource;
        private Color _foregroundBrushColor;
        private SolidColorBrush.Resource? _backgroundBrushResource;
        private Color _backgroundBrushColor;
        private SolidColorBrush.Resource?[]? _intensityBrushes;
        private Color _intensityBrushBaseColor;

        private Composer? _composer;
        private Sound.Resource? _source;

        public Sound.Resource? Source => _source;

        // Cached mono PCM for the current window in absolute scene time.
        private float[] _cachedSamples = [];
        private int _cachedSampleRate;
        private TimeSpan _cachedStart;
        private TimeSpan _cachedDuration;
        private int _cachedSourceVersion = -1;
        private AudioVisualizerMode _cachedMode;
        private int _cachedFftSize;

        internal float[] CachedSamples => _cachedSamples;
        internal int CachedSampleRate => _cachedSampleRate;
        internal TimeSpan CachedStart => _cachedStart;
        internal TimeSpan CachedDuration => _cachedDuration;

        partial void PostUpdate(AudioVisualizerDrawable obj, CompositionContext context)
        {
            EnsureBrushes(context);
            if (DisplayMode == AudioVisualizerMode.Spectrogram)
            {
                EnsureIntensityBrushes(context);
            }

            // 音声処理は専用コンテキストで実行し、MediaReader 等のリソース共有を無効化する。
            // これにより、プレビュー/エンコード側が保持する共有カウンタを visualizer 側の読み出しで汚染しない。
            var audioContext = new CompositionContext(context.Time) { DisableResourceShare = true };
            bool sourceUpdateOnly = true;
            CompareAndUpdateObject(audioContext, obj.Source, ref _source, ref sourceUpdateOnly);

            Sound.Resource? soundResource = _source;
            if (soundResource == null || soundResource.IsDisposed)
            {
                _cachedSamples = [];
                _cachedSampleRate = 0;
                _cachedDuration = TimeSpan.Zero;
                _cachedSourceVersion = -1;
                Version++;
                return;
            }

            EnsureSamplesComposed(soundResource, audioContext);
            Version++;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _foregroundBrushResource?.Dispose();
                _backgroundBrushResource?.Dispose();
                DisposeIntensityBrushes();
                _composer?.Dispose();
                _source?.Dispose();
            }
            _foregroundBrushResource = null;
            _backgroundBrushResource = null;
            _intensityBrushes = null;
            _composer = null;
            _source = null;
            _cachedSamples = [];
        }

        private void DisposeIntensityBrushes()
        {
            if (_intensityBrushes == null) return;
            for (int i = 0; i < _intensityBrushes.Length; i++)
            {
                _intensityBrushes[i]?.Dispose();
                _intensityBrushes[i] = null;
            }
        }

        private void EnsureIntensityBrushes(CompositionContext context)
        {
            Color baseColor = ForegroundColor;
            if (_intensityBrushes != null && _intensityBrushBaseColor == baseColor) return;

            DisposeIntensityBrushes();
            _intensityBrushes = new SolidColorBrush.Resource?[IntensityLevels];
            for (int i = 0; i < IntensityLevels; i++)
            {
                byte alpha = (byte)Math.Clamp(baseColor.A * (i + 1) / IntensityLevels, 0, 255);
                var color = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
                _intensityBrushes[i] = new SolidColorBrush(color).ToResource(context) as SolidColorBrush.Resource;
            }
            _intensityBrushBaseColor = baseColor;
        }

        internal SolidColorBrush.Resource? PickIntensityBrush(float normalized)
        {
            if (_intensityBrushes == null) return null;
            int idx = (int)(normalized * IntensityLevels);
            if (idx <= 0) return null;
            if (idx >= IntensityLevels) idx = IntensityLevels - 1;
            return _intensityBrushes[idx];
        }

        private void EnsureBrushes(CompositionContext context)
        {
            if (_foregroundBrushResource == null || _foregroundBrushColor != ForegroundColor)
            {
                _foregroundBrushResource?.Dispose();
                _foregroundBrushResource = new SolidColorBrush(ForegroundColor).ToResource(context) as SolidColorBrush.Resource;
                _foregroundBrushColor = ForegroundColor;
            }

            if (_backgroundBrushResource == null || _backgroundBrushColor != BackgroundColor)
            {
                _backgroundBrushResource?.Dispose();
                _backgroundBrushResource = new SolidColorBrush(BackgroundColor).ToResource(context) as SolidColorBrush.Resource;
                _backgroundBrushColor = BackgroundColor;
            }
        }

        private void EnsureSamplesComposed(Sound.Resource soundResource, CompositionContext context)
        {
            _composer ??= new Composer { SampleRate = DefaultComposerSampleRate };
            int rate = _composer.SampleRate;

            TimeSpan windowSeconds = TimeSpan.FromSeconds(Math.Max(0.01, WindowSeconds));
            int effectiveFftSize = Fft.ClampToPowerOfTwo(FftSize);
            TimeSpan targetStart;
            TimeSpan targetDuration;

            switch (DisplayMode)
            {
                case AudioVisualizerMode.Waveform:
                    targetStart = context.Time - TimeSpan.FromTicks(windowSeconds.Ticks / 2);
                    targetDuration = windowSeconds;
                    break;

                case AudioVisualizerMode.Spectrum:
                    targetDuration = TimeSpan.FromSeconds((double)effectiveFftSize / rate);
                    targetStart = context.Time - targetDuration;
                    break;

                case AudioVisualizerMode.Spectrogram:
                    targetStart = context.Time - windowSeconds;
                    targetDuration = windowSeconds;
                    break;

                default:
                    targetStart = context.Time;
                    targetDuration = windowSeconds;
                    break;
            }

            if (targetDuration <= TimeSpan.Zero)
            {
                _cachedSamples = [];
                _cachedSampleRate = rate;
                _cachedStart = targetStart;
                _cachedDuration = TimeSpan.Zero;
                _cachedSourceVersion = soundResource.Version;
                _cachedMode = DisplayMode;
                _cachedFftSize = effectiveFftSize;
                return;
            }

            bool needsRecompose = _cachedSourceVersion != soundResource.Version
                || _cachedStart != targetStart
                || _cachedDuration != targetDuration
                || _cachedMode != DisplayMode
                || _cachedFftSize != effectiveFftSize
                || _cachedSampleRate != rate;

            if (!needsRecompose) return;

            var targetRange = new TimeRange(targetStart, targetDuration);
            Sound sound = soundResource.GetOriginal();
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(soundResource),
                sound.TimeRange,
                default);

            AudioBuffer? buffer = _composer.Compose(targetRange, frame);
            try
            {
                if (buffer == null || buffer.SampleCount == 0)
                {
                    _cachedSamples = [];
                }
                else
                {
                    int n = buffer.SampleCount;
                    var mono = new float[n];
                    Span<float> leftChannel = buffer.GetChannelData(0);
                    if (buffer.ChannelCount >= 2)
                    {
                        Span<float> rightChannel = buffer.GetChannelData(1);
                        for (int i = 0; i < n; i++)
                        {
                            mono[i] = (leftChannel[i] + rightChannel[i]) * 0.5f;
                        }
                    }
                    else
                    {
                        leftChannel.CopyTo(mono);
                    }
                    _cachedSamples = mono;
                    rate = buffer.SampleRate;
                }
            }
            finally
            {
                buffer?.Dispose();
            }

            _cachedSampleRate = rate;
            _cachedStart = targetStart;
            _cachedDuration = targetDuration;
            _cachedSourceVersion = soundResource.Version;
            _cachedMode = DisplayMode;
            _cachedFftSize = effectiveFftSize;
        }

        internal void Render(GraphicsContext2D context)
        {
            var bounds = new Rect(0, 0, Math.Max(1f, Width), Math.Max(1f, Height));

            if (_backgroundBrushResource != null && BackgroundColor.A > 0)
            {
                context.DrawRectangle(bounds, _backgroundBrushResource, null);
            }

            if (_foregroundBrushResource == null) return;

            switch (DisplayMode)
            {
                case AudioVisualizerMode.Waveform:
                    RenderWaveform(context, bounds);
                    break;
                case AudioVisualizerMode.Spectrum:
                    RenderSpectrum(context, bounds);
                    break;
                case AudioVisualizerMode.Spectrogram:
                    RenderSpectrogram(context, bounds);
                    break;
            }
        }

        private void RenderWaveform(GraphicsContext2D context, Rect bounds)
        {
            if (_cachedSamples.Length == 0 || _foregroundBrushResource == null) return;

            int barCount = Math.Max(1, BarCount);
            float[] minBuf = new float[barCount];
            float[] maxBuf = new float[barCount];
            SoundSamplingHelper.DownsampleMinMax(_cachedSamples, minBuf, maxBuf);

            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float centerY = height * 0.5f;
            float gain = Math.Max(0f, Gain);
            float slotWidth = width / barCount;
            float barWidth = Math.Max(1f, slotWidth - 0.5f);

            switch (WaveformStyle)
            {
                case Graphics.WaveformStyle.MinMaxBar:
                    for (int i = 0; i < barCount; i++)
                    {
                        float min = Math.Clamp(minBuf[i] * gain, -1f, 1f);
                        float max = Math.Clamp(maxBuf[i] * gain, -1f, 1f);
                        float topY = centerY - max * centerY;
                        float bottomY = centerY - min * centerY;
                        float barHeight = Math.Max(1f, bottomY - topY);
                        float x = i * slotWidth;
                        context.DrawRectangle(new Rect(x, topY, barWidth, barHeight), _foregroundBrushResource, null);
                    }
                    break;

                case Graphics.WaveformStyle.Line:
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

                            // Draw horizontal segment centered at y
                            context.DrawRectangle(new Rect(x, y - halfThick, barWidth, lineThickness), _foregroundBrushResource, null);

                            // Draw vertical segment bridging previous y to current y for continuity
                            if (prevY.HasValue)
                            {
                                float minY = Math.Min(prevY.Value, y);
                                float maxY = Math.Max(prevY.Value, y);
                                if (maxY - minY > lineThickness)
                                {
                                    context.DrawRectangle(new Rect(x - halfThick, minY, lineThickness, maxY - minY), _foregroundBrushResource, null);
                                }
                            }
                            prevY = y;
                        }
                        break;
                    }

                case Graphics.WaveformStyle.FilledMirror:
                    for (int i = 0; i < barCount; i++)
                    {
                        float abs = Math.Max(Math.Abs(minBuf[i]), Math.Abs(maxBuf[i]));
                        float magnitude = Math.Clamp(abs * gain, 0f, 1f);
                        float halfHeight = magnitude * centerY;
                        float topY = centerY - halfHeight;
                        float barHeight = Math.Max(1f, halfHeight * 2f);
                        float x = i * slotWidth;
                        context.DrawRectangle(new Rect(x, topY, barWidth, barHeight), _foregroundBrushResource, null);
                    }
                    break;
            }
        }

        private void RenderSpectrum(GraphicsContext2D context, Rect bounds)
        {
            if (_foregroundBrushResource == null
                || _cachedSampleRate <= 0
                || _cachedSamples.Length == 0)
            {
                return;
            }

            int fftSize = Fft.ClampToPowerOfTwo(FftSize);
            if (fftSize < 2) return;

            float[] real = new float[fftSize];
            float[] imag = new float[fftSize];

            // Extract last fftSize samples (ending at requested position)
            int sampleCount = _cachedSamples.Length;
            int copy = Math.Min(sampleCount, fftSize);
            int srcStart = sampleCount - copy;
            int dstStart = fftSize - copy;
            if (srcStart < 0) srcStart = 0;
            for (int i = 0; i < copy; i++)
            {
                real[dstStart + i] = _cachedSamples[srcStart + i];
            }

            Fft.ApplyHann(real);
            Fft.Forward(real, imag);

            int bins = fftSize / 2;
            float[] mags = new float[bins];
            Fft.Magnitudes(real, imag, mags);

            float reference = fftSize * 0.5f;
            float gain = Math.Max(0f, Gain);
            float floorDb = FloorDb;
            float width = (float)bounds.Width;
            float height = (float)bounds.Height;

            int barCount = Math.Min(Math.Max(1, BarCount), bins);
            float slotWidth = width / barCount;
            float barWidth = Math.Max(1f, slotWidth - 0.5f);
            bool logarithmic = LogarithmicFrequency;

            float fMax = _cachedSampleRate * 0.5f;
            float fMin = Math.Max(20f, fMax / bins);

            for (int i = 0; i < barCount; i++)
            {
                int binLow;
                int binHigh;
                if (logarithmic)
                {
                    double freqLow = fMin * Math.Pow(fMax / fMin, (double)i / barCount);
                    double freqHigh = fMin * Math.Pow(fMax / fMin, (double)(i + 1) / barCount);
                    binLow = (int)Math.Floor(freqLow / fMax * bins);
                    binHigh = (int)Math.Ceiling(freqHigh / fMax * bins);
                }
                else
                {
                    binLow = i * bins / barCount;
                    binHigh = (i + 1) * bins / barCount;
                }

                if (binHigh <= binLow) binHigh = binLow + 1;
                if (binHigh > bins) binHigh = bins;
                if (binLow < 0) binLow = 0;

                float maxMag = 0f;
                for (int b = binLow; b < binHigh; b++)
                {
                    if (mags[b] > maxMag) maxMag = mags[b];
                }

                float db = Fft.MagnitudeToDb(maxMag * gain, reference);
                float normalized = (db - floorDb) / (0f - floorDb);
                normalized = Math.Clamp(normalized, 0f, 1f);

                float barHeight = Math.Max(1f, normalized * height);
                float x = i * slotWidth;
                float y = height - barHeight;
                context.DrawRectangle(new Rect(bounds.X + x, bounds.Y + y, barWidth, barHeight), _foregroundBrushResource, null);
            }
        }

        private void RenderSpectrogram(GraphicsContext2D context, Rect bounds)
        {
            if (_foregroundBrushResource == null || _cachedSampleRate <= 0 || _cachedSamples.Length == 0) return;

            int fftSize = Fft.ClampToPowerOfTwo(FftSize);
            if (fftSize < 2) return;

            int columns = Math.Max(2, TimeColumns);
            int bins = fftSize / 2;
            float width = (float)bounds.Width;
            float height = (float)bounds.Height;
            float colWidth = width / columns;
            float binHeight = height / bins;
            float reference = fftSize * 0.5f;
            float gain = Math.Max(0f, Gain);
            float floorDb = FloorDb;

            float[] real = new float[fftSize];
            float[] imag = new float[fftSize];
            float[] mags = new float[bins];

            int sampleCount = _cachedSamples.Length;

            // Map columns over the cached window chronologically. Each column's center maps to a sample index.
            // Early columns use oldest samples, last column uses newest samples (= requested position).
            for (int col = 0; col < columns; col++)
            {
                float normalizedT = (col + 0.5f) / columns;
                int centerSample = (int)(normalizedT * sampleCount);

                Array.Clear(imag, 0, fftSize);
                SoundSamplingHelper.ExtractWindow(_cachedSamples, centerSample, real);
                Fft.ApplyHann(real);
                Fft.Forward(real, imag);
                Fft.Magnitudes(real, imag, mags);

                float colX = bounds.X + col * colWidth;
                float drawColWidth = Math.Max(1f, colWidth + 0.5f);

                for (int bin = 0; bin < bins; bin++)
                {
                    float db = Fft.MagnitudeToDb(mags[bin] * gain, reference);
                    float normalized = (db - floorDb) / (0f - floorDb);
                    normalized = Math.Clamp(normalized, 0f, 1f);
                    SolidColorBrush.Resource? brush = PickIntensityBrush(normalized);
                    if (brush == null) continue;

                    // Higher frequency = top
                    float y = bounds.Y + height - (bin + 1) * binHeight;
                    context.DrawRectangle(
                        new Rect(colX, y, drawColWidth, Math.Max(1f, binHeight + 0.5f)),
                        brush,
                        null);
                }
            }
        }
    }
}
