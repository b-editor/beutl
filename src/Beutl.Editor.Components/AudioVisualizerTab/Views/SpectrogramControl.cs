using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public sealed class SpectrogramControl : AudioVisualizerControlBase
{
    public static readonly StyledProperty<int> FftSizeProperty =
        AvaloniaProperty.Register<SpectrogramControl, int>(nameof(FftSize), 1024);

    public static readonly StyledProperty<float> MinDecibelsProperty =
        AvaloniaProperty.Register<SpectrogramControl, float>(nameof(MinDecibels), -90f);

    public static readonly StyledProperty<float> WindowSecondsProperty =
        AvaloniaProperty.Register<SpectrogramControl, float>(nameof(WindowSeconds), 4f);

    private const int TimeColumns = 192;
    private const int VerticalBands = 96;

    private float[] _samplesL = [];
    private float[] _samplesR = [];
    private float[] _mono = [];
    private float[] _real = [];
    private float[] _imag = [];
    private float[] _mags = [];
    private readonly ImmutableSolidColorBrush?[] _brushCache = new ImmutableSolidColorBrush?[256];
    private Color _brushCacheBaseColor;

    public int FftSize
    {
        get => GetValue(FftSizeProperty);
        set => SetValue(FftSizeProperty, value);
    }

    public float MinDecibels
    {
        get => GetValue(MinDecibelsProperty);
        set => SetValue(MinDecibelsProperty, value);
    }

    public float WindowSeconds
    {
        get => GetValue(WindowSecondsProperty);
        set => SetValue(WindowSecondsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);

        AudioSampleRingBuffer? buffer = RingBuffer;
        if (buffer == null || bounds.Width < 8 || bounds.Height < 8) return;

        int sampleRate = buffer.SampleRate;
        if (sampleRate <= 0) return;

        int fftSize = Fft.ClampToPowerOfTwo(FftSize, min: 256, max: 4096);
        float windowSec = Math.Clamp(WindowSeconds, 0.5f, 30f);
        int totalSamples = Math.Max(fftSize * 2, (int)(windowSec * sampleRate));

        EnsureBuffers(fftSize, totalSamples);

        // Read a continuous window ending at the playhead — same convention used by
        // SpectrumControl so the rightmost column visually aligns with "now".
        int got = buffer.ReadAroundTime(PlayheadTime, _samplesL, _samplesR, totalSamples);
        if (got < fftSize) return;

        Span<float> mono = _mono.AsSpan(0, totalSamples);
        for (int i = 0; i < totalSamples; i++)
        {
            mono[i] = 0.5f * (_samplesL[i] + _samplesR[i]);
        }

        Span<float> real = _real.AsSpan(0, fftSize);
        Span<float> imag = _imag.AsSpan(0, fftSize);
        int bins = fftSize / 2;
        Span<float> mags = _mags.AsSpan(0, bins);

        int columns = TimeColumns;
        double colWidth = bounds.Width / columns;
        double rowHeight = bounds.Height / VerticalBands;
        float referenceMag = fftSize * 0.5f;
        float minDb = MinDecibels;
        float rangeDb = -minDb;

        Color baseColor = (PrimaryBrush as ISolidColorBrush)?.Color ?? Colors.LimeGreen;
        if (baseColor != _brushCacheBaseColor)
        {
            Array.Clear(_brushCache);
            _brushCacheBaseColor = baseColor;
        }

        for (int col = 0; col < columns; col++)
        {
            float t = (col + 0.5f) / columns;
            int center = (int)(t * totalSamples);

            // Engine's helper applies the same zero-padded window extraction used by
            // AudioSpectrogramDrawable, so column alignment matches the renderer.
            SoundSamplingHelper.ExtractWindow(mono, center, real);
            imag.Clear();
            Fft.ApplyHann(real);
            Fft.Forward(real, imag);
            Fft.Magnitudes(real, imag, mags);

            double colX = col * colWidth;
            double drawColWidth = Math.Max(1.0, colWidth + 0.5);

            for (int b = 0; b < VerticalBands; b++)
            {
                // Logarithmic frequency axis: row b covers bin range [bins^(b/B), bins^((b+1)/B)]
                int binLow = Math.Max(1, (int)Math.Floor(Math.Pow(bins, (double)b / VerticalBands)));
                int binHigh = Math.Min(bins, (int)Math.Ceiling(Math.Pow(bins, (b + 1.0) / VerticalBands)));
                if (binHigh <= binLow) binHigh = Math.Min(bins, binLow + 1);

                float maxMag = 0f;
                for (int k = binLow; k < binHigh; k++)
                {
                    if (mags[k] > maxMag) maxMag = mags[k];
                }

                float db = MathF.Max(Fft.MagnitudeToDb(maxMag, referenceMag), minDb);
                float norm = (db - minDb) / rangeDb;
                if (norm <= 0f) continue;

                byte alpha = (byte)Math.Clamp(norm * 255f, 0f, 255f);
                if (alpha == 0) continue;

                // Higher frequencies on top: row b=0 → bottom, b=B-1 → top.
                double y = bounds.Height - (b + 1) * rowHeight;
                ImmutableSolidColorBrush brush = _brushCache[alpha]
                    ??= new ImmutableSolidColorBrush(
                        Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                context.FillRectangle(
                    brush,
                    new Rect(colX, y, drawColWidth, Math.Max(1.0, rowHeight + 0.5)));
            }
        }
    }

    private void EnsureBuffers(int fftSize, int totalSamples)
    {
        if (_samplesL.Length < totalSamples)
        {
            _samplesL = new float[totalSamples];
            _samplesR = new float[totalSamples];
            _mono = new float[totalSamples];
        }
        if (_real.Length < fftSize)
        {
            _real = new float[fftSize];
            _imag = new float[fftSize];
            _mags = new float[fftSize / 2];
        }
    }
}
