using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
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
        Shape.CurrentValue = new BarSpectrumShape();
    }

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Shape), ResourceType = typeof(GraphicsStrings))]
    public IProperty<SpectrumShape?> Shape { get; } = Property.Create<SpectrumShape?>();

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_BarCount), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 10000)]
    public IProperty<int> BarCount { get; } = Property.Create(128);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FftSize), ResourceType = typeof(GraphicsStrings))]
    [Range(64, 16384)]
    public IProperty<int> FftSize { get; } = Property.Create(1024);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_LogarithmicFrequency), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> LogarithmicFrequency { get; } = Property.Create(true);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FloorDb), ResourceType = typeof(GraphicsStrings))]
    [Range(-200f, 0f)]
    public IProperty<float> FloorDb { get; } = Property.CreateAnimatable(-80f);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_Smoothing), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, 99f)]
    public IProperty<float> Smoothing { get; } = Property.CreateAnimatable(85f);

    public new partial class Resource
    {
        private float[] _fftReal = [];
        private float[] _fftImag = [];
        private float[] _fftMagnitudes = [];
        private float[] _smoothedMagnitudes = [];
        private float[] _normalizedBars = [];

        protected override (TimeSpan Start, TimeSpan Duration) ComputeSampleWindow(TimeSpan currentTime)
        {
            int effectiveFftSize = Fft.ClampToPowerOfTwo(FftSize);
            TimeSpan duration = TimeSpan.FromSeconds((double)effectiveFftSize / ComposerSampleRate);
            return (currentTime - duration, duration);
        }

        protected override void RenderForeground(ImmediateCanvas canvas, Rect bounds)
        {
            if (CachedSampleLength == 0 || ForegroundBrush is null || CachedSampleRate <= 0) return;

            SpectrumShape.Resource? shapeResource = Shape;
            if (shapeResource is null) return;

            int fftSize = Fft.ClampToPowerOfTwo(FftSize);
            if (fftSize < 2) return;

            int bins = fftSize / 2;
            if (_fftReal.Length < fftSize) _fftReal = new float[fftSize];
            if (_fftImag.Length < fftSize) _fftImag = new float[fftSize];
            if (_fftMagnitudes.Length < bins) _fftMagnitudes = new float[bins];
            Span<float> real = _fftReal.AsSpan(0, fftSize);
            Span<float> imag = _fftImag.AsSpan(0, fftSize);
            Span<float> mags = _fftMagnitudes.AsSpan(0, bins);

            real.Clear();
            imag.Clear();

            ReadOnlySpan<float> samples = CachedSampleSpan;
            int sampleCount = samples.Length;
            int copy = Math.Min(sampleCount, fftSize);
            int srcStart = Math.Max(0, sampleCount - copy);
            int dstStart = fftSize - copy;
            samples.Slice(srcStart, copy).CopyTo(real.Slice(dstStart, copy));

            Fft.ApplyHann(real);
            Fft.Forward(real, imag);
            Fft.Magnitudes(real, imag, mags);

            float reference = fftSize * 0.5f;
            float gain = Math.Max(0f, Gain);
            float floorDb = FloorDb;

            int barCount = Math.Min(Math.Max(1, BarCount), bins);
            bool logarithmic = LogarithmicFrequency;

            if (_smoothedMagnitudes.Length < barCount)
            {
                _smoothedMagnitudes = new float[barCount];
            }
            if (_normalizedBars.Length < barCount)
            {
                _normalizedBars = new float[barCount];
            }
            Span<float> smoothed = _smoothedMagnitudes.AsSpan(0, barCount);
            Span<float> normalized = _normalizedBars.AsSpan(0, barCount);
            float smoothing = Math.Clamp(Smoothing / 100f, 0f, 0.99f);

            float fMax = CachedSampleRate * 0.5f;
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

                // バンド内は RMS で集計するとピーク値より滑らかに変化する
                float sumSq = 0f;
                int count = binHigh - binLow;
                for (int b = binLow; b < binHigh; b++)
                {
                    sumSq += mags[b] * mags[b];
                }
                float rawMag = count > 0 ? MathF.Sqrt(sumSq / count) : 0f;

                // 高速アタック + 緩やかリリース (ピークメーター方式)
                float prev = smoothed[i];
                float smoothedMag = rawMag > prev
                    ? rawMag
                    : prev * smoothing + rawMag * (1f - smoothing);
                smoothed[i] = smoothedMag;

                float db = Fft.MagnitudeToDb(smoothedMag * gain, reference);
                float n = (db - floorDb) / (0f - floorDb);
                normalized[i] = Math.Clamp(n, 0f, 1f);
            }

            shapeResource.Render(canvas, bounds, normalized, ForegroundBrush, ForegroundColor);
        }
    }
}
