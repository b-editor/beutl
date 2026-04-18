using Avalonia;
using Avalonia.Media;
using Beutl.Audio.Graph;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public sealed class SpectrumControl : AudioVisualizerControlBase
{
    public static readonly StyledProperty<int> FftSizeProperty =
        AvaloniaProperty.Register<SpectrumControl, int>(nameof(FftSize), 2048);

    public static readonly StyledProperty<float> MinDecibelsProperty =
        AvaloniaProperty.Register<SpectrumControl, float>(nameof(MinDecibels), -90f);

    private float[] _samplesL = [];
    private float[] _samplesR = [];
    private float[] _real = [];
    private float[] _imag = [];
    private float[] _magnitudes = [];
    private float[] _smoothed = [];

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

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);

        AudioSampleRingBuffer? buffer = RingBuffer;
        if (buffer == null || bounds.Width < 8 || bounds.Height < 8) return;

        int n = Fft.ClampToPowerOfTwo(FftSize, min: 256, max: 8192);
        EnsureBuffers(n);

        int got = buffer.ReadAroundTime(PlayheadTime, _samplesL, _samplesR, n);
        if (got < n / 2) return;

        Span<float> real = _real.AsSpan(0, n);
        Span<float> imag = _imag.AsSpan(0, n);
        for (int i = 0; i < n; i++)
        {
            real[i] = 0.5f * (_samplesL[i] + _samplesR[i]);
        }
        imag.Clear();

        Fft.ApplyHann(real);
        Fft.Forward(real, imag);

        int bins = n / 2;
        Span<float> mags = _magnitudes.AsSpan(0, bins);
        Fft.Magnitudes(real, imag, mags);

        float referenceMag = n * 0.5f;
        float minDb = MinDecibels;
        float rangeDb = -minDb;

        for (int i = 0; i < bins; i++)
        {
            float db = MathF.Max(Fft.MagnitudeToDb(mags[i], referenceMag), minDb);
            float norm = (db - minDb) / rangeDb;
            if (norm < 0) norm = 0;
            _smoothed[i] = _smoothed[i] * 0.55f + norm * 0.45f;
        }

        // Logarithmic bar layout: groups bins into ~64 display bands.
        int bands = Math.Min(96, bins);
        double barWidth = bounds.Width / bands;
        double height = bounds.Height - 2;
        for (int b = 0; b < bands; b++)
        {
            double lo = Math.Pow(bins, b / (double)bands);
            double hi = Math.Pow(bins, (b + 1) / (double)bands);
            int start = Math.Max(1, (int)Math.Floor(lo));
            int end = Math.Min(bins, (int)Math.Ceiling(hi));
            if (end <= start) end = Math.Min(bins, start + 1);

            float peak = 0f;
            for (int k = start; k < end; k++)
            {
                float v = _smoothed[k];
                if (v > peak) peak = v;
            }

            double h = peak * height;
            double x = b * barWidth;
            var rect = new Rect(x + 0.5, bounds.Height - h, Math.Max(0.5, barWidth - 1.0), h);
            context.FillRectangle(PrimaryBrush, rect);
        }
    }

    private void EnsureBuffers(int n)
    {
        if (_samplesL.Length < n)
        {
            _samplesL = new float[n];
            _samplesR = new float[n];
            _real = new float[n];
            _imag = new float[n];
            _magnitudes = new float[n / 2];
            _smoothed = new float[n / 2];
        }
    }
}
