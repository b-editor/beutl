using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Beutl.Editor.Components.AudioVisualizerTab.Utilities;
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
    private Complex[] _complex = [];
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

        int n = NormalizeFftSize(FftSize);
        EnsureBuffers(n);

        int got = buffer.ReadAroundTime(PlayheadTime, _samplesL, _samplesR, n);
        if (got < n / 2) return;

        for (int i = 0; i < n; i++)
        {
            _complex[i] = new Complex(0.5f * (_samplesL[i] + _samplesR[i]), 0);
        }

        var mono = new Span<float>(_samplesL, 0, n);
        // Apply window on mono view (reuse _samplesL buffer; we already combined into _complex)
        // Instead: reconstruct mono for window from complex
        for (int i = 0; i < n; i++) mono[i] = (float)_complex[i].Real;
        Fft.ApplyHannWindow(mono);
        for (int i = 0; i < n; i++) _complex[i] = new Complex(mono[i], 0);

        Fft.Forward(_complex);

        int bins = n / 2;
        float referenceMag = n * 0.5f;
        float minDb = MinDecibels;
        float rangeDb = -minDb;

        for (int i = 0; i < bins; i++)
        {
            float mag = Fft.Magnitude(_complex[i]);
            float db = Fft.ToDecibels(mag, referenceMag, minDb);
            float norm = (db - minDb) / rangeDb;
            if (norm < 0) norm = 0;
            _magnitudes[i] = norm;
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
            _complex = new Complex[n];
            _magnitudes = new float[n / 2];
            _smoothed = new float[n / 2];
        }
    }

    private static int NormalizeFftSize(int requested)
    {
        int n = 2;
        while (n < requested) n <<= 1;
        if (n < 256) n = 256;
        if (n > 8192) n = 8192;
        return n;
    }
}
