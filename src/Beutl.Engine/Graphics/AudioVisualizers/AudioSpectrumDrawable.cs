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
    public IProperty<SpectrumShape?> Shape { get; } = Property.Create<SpectrumShape?>();

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_BarCount), ResourceType = typeof(GraphicsStrings))]
    [Range(1, 10000)]
    public IProperty<int> BarCount { get; } = Property.Create(128);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FftSize), ResourceType = typeof(GraphicsStrings))]
    [Range(64, 16384)]
    public IProperty<int> FftSize { get; } = Property.Create(1024);

    [Display(Name = nameof(GraphicsStrings.AudioVisualizer_FrequencyScale), ResourceType = typeof(GraphicsStrings))]
    public IProperty<FrequencyScale> FrequencyScale { get; } = Property.Create(AudioVisualizers.FrequencyScale.Logarithmic);

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
            if (CachedSampleLength == 0 || Fill is null || CachedSampleRate <= 0) return;

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
            // FloorDb は Range(-200, 0) だが、0 に設定されると (0 - floorDb) が 0 除算になるため -0.001 以下にクランプする
            float floorDb = MathF.Min(FloorDb, -0.001f);

            int barCount = Math.Min(Math.Max(1, BarCount), bins);
            FrequencyScale freqScale = FrequencyScale;

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
            double melMin = freqScale == FrequencyScale.Mel ? 2595.0 * Math.Log10(1 + fMin / 700.0) : 0;
            double melMax = freqScale == FrequencyScale.Mel ? 2595.0 * Math.Log10(1 + fMax / 700.0) : 0;

            for (int i = 0; i < barCount; i++)
            {
                int binLow;
                int binHigh;
                switch (freqScale)
                {
                    case FrequencyScale.Logarithmic:
                        {
                            double freqLow = fMin * Math.Pow(fMax / fMin, (double)i / barCount);
                            double freqHigh = fMin * Math.Pow(fMax / fMin, (double)(i + 1) / barCount);
                            binLow = (int)Math.Floor(freqLow / fMax * bins);
                            binHigh = (int)Math.Ceiling(freqHigh / fMax * bins);
                            break;
                        }
                    case FrequencyScale.Mel:
                        {
                            double m1 = melMin + (melMax - melMin) * i / barCount;
                            double m2 = melMin + (melMax - melMin) * (i + 1) / barCount;
                            double f1 = 700.0 * (Math.Pow(10, m1 / 2595.0) - 1);
                            double f2 = 700.0 * (Math.Pow(10, m2 / 2595.0) - 1);
                            binLow = (int)Math.Floor(f1 / fMax * bins);
                            binHigh = (int)Math.Ceiling(f2 / fMax * bins);
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

            shapeResource.Render(canvas, bounds, normalized, Fill);
        }
    }
}
