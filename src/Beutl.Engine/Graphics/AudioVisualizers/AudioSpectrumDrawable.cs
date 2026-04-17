using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

[Display(Name = nameof(GraphicsStrings.AudioSpectrum), ResourceType = typeof(GraphicsStrings))]
public sealed partial class AudioSpectrumDrawable : AudioVisualizerDrawable
{
    public AudioSpectrumDrawable()
    {
        ScanProperties<AudioSpectrumDrawable>();
    }

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

    public new partial class Resource
    {
        protected override (TimeSpan Start, TimeSpan Duration) ComputeSampleWindow(TimeSpan currentTime)
        {
            int effectiveFftSize = Fft.ClampToPowerOfTwo(FftSize);
            TimeSpan duration = TimeSpan.FromSeconds((double)effectiveFftSize / ComposerSampleRate);
            return (currentTime - duration, duration);
        }

        protected override void RenderForeground(GraphicsContext2D context, Rect bounds)
        {
            float[] samples = CachedSamples;
            if (samples.Length == 0 || ForegroundBrush is null || CachedSampleRate <= 0) return;

            int fftSize = Fft.ClampToPowerOfTwo(FftSize);
            if (fftSize < 2) return;

            var real = new float[fftSize];
            var imag = new float[fftSize];

            int sampleCount = samples.Length;
            int copy = Math.Min(sampleCount, fftSize);
            int srcStart = Math.Max(0, sampleCount - copy);
            int dstStart = fftSize - copy;
            for (int i = 0; i < copy; i++)
            {
                real[dstStart + i] = samples[srcStart + i];
            }

            Fft.ApplyHann(real);
            Fft.Forward(real, imag);

            int bins = fftSize / 2;
            var mags = new float[bins];
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
                context.DrawRectangle(new Rect(bounds.X + x, bounds.Y + y, barWidth, barHeight), ForegroundBrush, null);
            }
        }
    }
}
