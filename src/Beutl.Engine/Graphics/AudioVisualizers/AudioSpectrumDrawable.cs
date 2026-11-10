using System.ComponentModel.DataAnnotations;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.AudioSpectrum), ResourceType = typeof(GraphicsStrings))]
public sealed partial class AudioSpectrumDrawable : AudioVisualizerDrawable
{
    public AudioSpectrumDrawable()
    {
        ScanProperties<AudioSpectrumDrawable>();
        Shape.Value = new BarSpectrumShape();

        // 編集頻度順に並べ替え: Source → 見た目(Shape/Fill) → 信号調整(Gain/FloorDb) → 応答(Smoothing) → 周波数設定(Scale/BarCount) → 解像度(FftSize) → サイズ
        MoveProperty(Source, 0);
        MoveProperty(Shape, 1);
        MoveProperty(Fill, 2);
        MoveProperty(Gain, 3);
        MoveProperty(FloorDb, 4);
        MoveProperty(Smoothing, 5);
        MoveProperty(FrequencyScale, 6);
        MoveProperty(BarCount, 7);
        MoveProperty(FftSize, 8);
        MoveProperty(Width, 9);
        MoveProperty(Height, 10);
    }

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Shape), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<SpectrumShape?> Shape { get; } = Property.CreateExpression<SpectrumShape?>();

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_BarCount), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 10000)]
    public IExpressionProperty<int> BarCount { get; } = Property.CreateExpression(128);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FftSize), ResourceType = typeof(GraphicsStrings))]
    [Range(64, 16384)]
    public IExpressionProperty<int> FftSize { get; } = Property.CreateExpression(1024);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FrequencyScale), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<FrequencyScale> FrequencyScale { get; } = Property.CreateExpression(AudioVisualizers.FrequencyScale.Logarithmic);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FloorDb), ResourceType = typeof(GraphicsStrings))]
    [Range(-200f, 0f)]
    public IExpressionProperty<float> FloorDb { get; } = Property.CreateAnimatable(-80f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Smoothing), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 99f)]
    public IExpressionProperty<float> Smoothing { get; } = Property.CreateAnimatable(85f);

    public new partial class Resource
    {
        private float[] _fftReal = [];
        private float[] _fftImag = [];
        private float[] _fftMagnitudes = [];
        private float[] _normalizedBars = [];
        private int? _renderedSpectrumVersion;
        private int _renderedBarCount;
        private int _sampleWindowFftSize;
        private float _sampleWindowSmoothing;
        private float _sampleWindowFloorDb;
        private long _sampleEnd;
        private long _firstHistoryFrame;
        private long _lastHistoryFrame;
        private long[] _historyFrames = [];
        private float[] _historySamples = [];
        private float[] _historyBars = [];
        private (int FftSize, int BarCount, int SampleRate, FrequencyScale Scale, float Gain, float FloorDb) _historySettings;

        // The base composes the sample window before this class reconciles FftSize.
        partial void PreReconcile(AudioSpectrumDrawable obj, CompositionContext context)
        {
            _sampleWindowFftSize = context.Get(obj.FftSize);
            _sampleWindowSmoothing = context.Get(obj.Smoothing);
            _sampleWindowFloorDb = context.Get(obj.FloorDb);
        }

        protected override (MediaTime Start, MediaTime Duration) ComputeSampleWindow(MediaTime currentTime)
        {
            int effectiveFftSize = Fft.ClampToPowerOfTwo(_sampleWindowFftSize);
            int rate = ComposerSampleRate;
            int hop = effectiveFftSize / 2;
            _sampleEnd = currentTime.ToSamples(rate, MidpointRounding.AwayFromZero);
            long history = (long)Math.Ceiling(ReleaseSeconds(_sampleWindowSmoothing, _sampleWindowFloorDb) * rate);
            long firstSample = _sampleEnd - history;
            _firstHistoryFrame = (long)Math.Ceiling((double)firstSample / hop);
            _lastHistoryFrame = (long)Math.Floor((double)_sampleEnd / hop);
            long start = Math.Min(_sampleEnd, _firstHistoryFrame * hop) - effectiveFftSize;
            return (MediaTime.FromSamples(start, rate), MediaTime.FromSamples(_sampleEnd - start, rate));
        }

        protected override void RenderForeground(ImmediateCanvas canvas, Rect bounds)
        {
            if (CachedSampleLength == 0 || Fill is null || CachedSampleRate <= 0) return;
            Shape?.Render(new SpectrumRenderContext(canvas, bounds, GetNormalizedBars(), Fill));
        }

        internal ReadOnlySpan<float> GetNormalizedBars()
        {
            if (CachedSampleLength == 0 || CachedSampleRate <= 0) return [];
            if (_renderedSpectrumVersion == Version)
                return _normalizedBars.AsSpan(0, _renderedBarCount);

            int fftSize = Fft.ClampToPowerOfTwo(FftSize);
            int barCount = Math.Clamp(BarCount, 1, fftSize / 2);
            if (_normalizedBars.Length < barCount)
                _normalizedBars = new float[barCount];
            Span<float> normalized = _normalizedBars.AsSpan(0, barCount);
            CalculateBars(PrepareWindow(fftSize, _sampleEnd), normalized);

            double releaseSeconds = ReleaseSeconds(Smoothing, FloorDb);
            if (releaseSeconds > 0 && _firstHistoryFrame <= _lastHistoryFrame)
            {
                PrepareHistory(fftSize, barCount, releaseSeconds);
                int hop = fftSize / 2;
                for (long frame = _firstHistoryFrame; frame <= _lastHistoryFrame; frame++)
                {
                    long end = frame * hop;
                    ReadOnlySpan<float> bars = GetHistoryBars(frame, end, fftSize, barCount);
                    float release = (float)((_sampleEnd - end) / (CachedSampleRate * releaseSeconds));
                    for (int i = 0; i < barCount; i++)
                        normalized[i] = Math.Max(normalized[i], bars[i] - release);
                }
            }

            _renderedBarCount = barCount;
            _renderedSpectrumVersion = Version;
            return normalized;
        }

        // Keep the release speed of the old 30 fps peak hold, expressed in seconds. A full-height bar reaches
        // the display floor in this time, so older audio cannot affect it. No previously rendered frame is input.
        private static double ReleaseSeconds(float smoothing, float floorDb)
        {
            double factor = Math.Clamp(smoothing / 100.0, 0, 0.99);
            return factor <= 0 ? 0 : Math.Min(floorDb, -0.001f) / (20 * Math.Log10(factor) * 30);
        }

        private void PrepareHistory(int fftSize, int barCount, double releaseSeconds)
        {
            var settings = (fftSize, barCount, CachedSampleRate, FrequencyScale, Gain, FloorDb);
            // Reserve the whole release window at once, including while playback is still near the source start.
            int frames = (int)Math.Ceiling(releaseSeconds * CachedSampleRate / (fftSize / 2)) + 2;
            if (settings != _historySettings || _historyFrames.Length < frames)
            {
                _historySettings = settings;
                _historyFrames = new long[frames];
                Array.Fill(_historyFrames, long.MinValue);
                _historySamples = new float[frames * fftSize];
                _historyBars = new float[frames * barCount];
            }
        }

        private ReadOnlySpan<float> GetHistoryBars(long frame, long end, int fftSize, int barCount)
        {
            int slot = (int)(frame % _historyFrames.Length);
            if (slot < 0) slot += _historyFrames.Length;
            Span<float> samples = PrepareWindow(fftSize, end);
            Span<float> previous = _historySamples.AsSpan(slot * fftSize, fftSize);
            Span<float> bars = _historyBars.AsSpan(slot * barCount, barCount);
            // Compare the actual audio as well as its position: source gains, effects and expressions can change
            // the samples without changing the FFT settings. Unchanged overlapping windows reuse their FFT.
            if (_historyFrames[slot] != frame || !samples.SequenceEqual(previous))
            {
                samples.CopyTo(previous);
                CalculateBars(samples, bars);
                _historyFrames[slot] = frame;
            }
            return bars;
        }

        private Span<float> PrepareWindow(int fftSize, long end)
        {
            if (_fftReal.Length < fftSize) _fftReal = new float[fftSize];
            if (_fftImag.Length < fftSize) _fftImag = new float[fftSize];
            if (_fftMagnitudes.Length < fftSize / 2) _fftMagnitudes = new float[fftSize / 2];
            Span<float> real = _fftReal.AsSpan(0, fftSize);
            long start = CachedStart.ToSamples(CachedSampleRate, MidpointRounding.AwayFromZero);
            SoundSamplingHelper.ExtractWindow(CachedSampleSpan, (int)(end - start) - fftSize / 2, real);
            return real;
        }

        private void CalculateBars(Span<float> real, Span<float> bars)
        {
            // Silent windows need no transform, and must stay at the baseline even below -160 dB.
            bool hasSignal = false;
            foreach (float sample in real)
            {
                if (sample == 0) continue;
                hasSignal = true;
                break;
            }
            if (!hasSignal)
            {
                bars.Clear();
                return;
            }

            int fftSize = real.Length;
            int bins = fftSize / 2;
            Span<float> imag = _fftImag.AsSpan(0, fftSize);
            Span<float> mags = _fftMagnitudes.AsSpan(0, bins);
            imag.Clear();
            Fft.ApplyHann(real);
            Fft.Forward(real, imag);
            Fft.Magnitudes(real, imag, mags);

            float reference = fftSize * 0.5f;
            float gain = Math.Max(0f, Gain);
            float floorDb = MathF.Min(FloorDb, -0.001f);
            FrequencyScale scale = FrequencyScale;
            FrequencyAxis axis = FrequencyAxis.Create(scale, CachedSampleRate, bins);
            for (int i = 0; i < bars.Length; i++)
            {
                ResolveBarBins(i, bars.Length, bins, scale, in axis, out int binLow, out int binHigh);
                float db = Fft.MagnitudeToDb(BandRms(mags, binLow, binHigh) * gain, reference);
                bars[i] = Math.Clamp((db - floorDb) / -floorDb, 0, 1);
            }
        }

        /// <summary>Resolves the bin range <c>[binLow, binHigh)</c> bar <paramref name="i"/> covers on the chosen scale.</summary>
        private static void ResolveBarBins(
            int i,
            int barCount,
            int bins,
            FrequencyScale freqScale,
            in FrequencyAxis axis,
            out int binLow,
            out int binHigh)
        {
            switch (freqScale)
            {
                case FrequencyScale.Logarithmic:
                    {
                        double freqLow = axis.FrequencyAt((double)i / barCount, freqScale);
                        double freqHigh = axis.FrequencyAt((double)(i + 1) / barCount, freqScale);
                        binLow = (int)Math.Floor(freqLow / axis.FMax * bins);
                        binHigh = (int)Math.Ceiling(freqHigh / axis.FMax * bins);
                        break;
                    }
                case FrequencyScale.Mel:
                    {
                        double m1 = axis.MelMin + (axis.MelMax - axis.MelMin) * i / barCount;
                        double m2 = axis.MelMin + (axis.MelMax - axis.MelMin) * (i + 1) / barCount;
                        double f1 = FrequencyAxis.MelToFrequency(m1);
                        double f2 = FrequencyAxis.MelToFrequency(m2);
                        binLow = (int)Math.Floor(f1 / axis.FMax * bins);
                        binHigh = (int)Math.Ceiling(f2 / axis.FMax * bins);
                        break;
                    }
                default:
                    binLow = i * bins / barCount;
                    binHigh = (i + 1) * bins / barCount;
                    break;
            }

            if (binHigh <= binLow) binHigh = binLow + 1;
            if (binHigh > bins) binHigh = bins;
            if (binLow < 0) binLow = 0;
        }

        private static float BandRms(ReadOnlySpan<float> mags, int binLow, int binHigh)
        {
            float sumSq = 0f;
            int count = binHigh - binLow;
            for (int b = binLow; b < binHigh; b++)
            {
                sumSq += mags[b] * mags[b];
            }
            return count > 0 ? MathF.Sqrt(sumSq / count) : 0f;
        }
    }
}
