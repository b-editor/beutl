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

    public static readonly StyledProperty<float> SmoothingProperty =
        AvaloniaProperty.Register<SpectrumControl, float>(nameof(Smoothing), 55f);

    private const double FrequencyAxisHeight = 14.0;
    private static readonly double[] s_frequencyTicks = [50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000];

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

    public float Smoothing
    {
        get => GetValue(SmoothingProperty);
        set => SetValue(SmoothingProperty, value);
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

        float smoothFactor = Math.Clamp(Smoothing, 0f, 95f) / 100f;
        float newWeight = 1f - smoothFactor;
        for (int i = 0; i < bins; i++)
        {
            float db = MathF.Max(Fft.MagnitudeToDb(mags[i], referenceMag), minDb);
            float norm = (db - minDb) / rangeDb;
            if (norm < 0) norm = 0;
            _smoothed[i] = _smoothed[i] * smoothFactor + norm * newWeight;
        }

        double plotHeight = Math.Max(0, bounds.Height - FrequencyAxisHeight);
        var plotBounds = new Rect(0, 0, bounds.Width, plotHeight);

        DrawFrequencyGrid(context, plotBounds, buffer.SampleRate, bins);
        DrawBars(context, plotBounds, bins);
        DrawFrequencyLabels(context, bounds, buffer.SampleRate, bins);
    }

    private void DrawBars(DrawingContext context, Rect plotBounds, int bins)
    {
        // Logarithmic bar layout: groups bins into ~96 display bands.
        int bands = Math.Min(96, bins);
        double barWidth = plotBounds.Width / bands;
        double height = plotBounds.Height - 2;
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
            var rect = new Rect(x + 0.5, plotBounds.Bottom - h, Math.Max(0.5, barWidth - 1.0), h);
            context.FillRectangle(PrimaryBrush, rect);
        }
    }

    private void DrawFrequencyGrid(DrawingContext context, Rect plotBounds, int sampleRate, int bins)
    {
        if (sampleRate <= 0 || bins < 2) return;

        var gridPen = new Pen(new SolidColorBrush(Colors.Gray, 0.35), 0.5)
        {
            DashStyle = DashStyle.Dash,
        };

        double nyquist = sampleRate * 0.5;
        foreach (double freq in s_frequencyTicks)
        {
            if (freq >= nyquist) break;
            double x = FrequencyToX(freq, plotBounds.Width, nyquist, bins);
            context.DrawLine(gridPen, new Point(x, plotBounds.Top), new Point(x, plotBounds.Bottom));
        }
    }

    private void DrawFrequencyLabels(DrawingContext context, Rect bounds, int sampleRate, int bins)
    {
        if (sampleRate <= 0 || bins < 2) return;

        IBrush textBrush = Foreground ?? Brushes.LightGray;
        double nyquist = sampleRate * 0.5;
        double labelY = bounds.Bottom - FrequencyAxisHeight + 1;
        double plotWidth = bounds.Width;

        foreach (double freq in s_frequencyTicks)
        {
            if (freq >= nyquist) break;
            string label = freq >= 1000 ? $"{freq / 1000:0.#}k" : $"{freq:0}";
            var formatted = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                9,
                textBrush);
            double x = FrequencyToX(freq, plotWidth, nyquist, bins) - formatted.Width / 2;
            x = Math.Clamp(x, 0, plotWidth - formatted.Width);
            context.DrawText(formatted, new Point(x, labelY));
        }
    }

    // Bars use Math.Pow(bins, b/bands) → log-space over bin index 1..bins.
    // Convert frequency → bin → x using the same mapping so ticks align with bar peaks.
    private static double FrequencyToX(double freq, double width, double nyquist, int bins)
    {
        double targetBin = freq / nyquist * bins;
        if (targetBin < 1) targetBin = 1;
        double t = Math.Log(targetBin) / Math.Log(bins);
        return Math.Clamp(t, 0, 1) * width;
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
