using Avalonia;
using Avalonia.Media;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public sealed class LevelMeterControl : AudioVisualizerControlBase
{
    private const int RmsWindowSamples = 1024;
    private const float MinDb = -60f;

    private float _peakLHold;
    private float _peakRHold;
    private DateTime _lastRenderTime = DateTime.UtcNow;

    public override void Render(DrawingContext context)
    {
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);

        AudioSampleRingBuffer? buffer = RingBuffer;
        if (buffer == null || bounds.Width < 20 || bounds.Height < 20) return;

        var (rmsL, rmsR, peakL, peakR) = buffer.ComputeMeters(RmsWindowSamples, PlayheadTime);
        DateTime now = DateTime.UtcNow;
        double elapsed = Math.Clamp((now - _lastRenderTime).TotalSeconds, 0, 0.5);
        _lastRenderTime = now;

        _peakLHold = DecayHold(_peakLHold, peakL, elapsed);
        _peakRHold = DecayHold(_peakRHold, peakR, elapsed);

        double padding = 4;
        double barWidth = (bounds.Width - padding * 3) / 2;
        double topInset = 10;
        double barHeight = bounds.Height - topInset - padding;

        DrawChannel(context, "L", padding, topInset, barWidth, barHeight, rmsL, _peakLHold);
        DrawChannel(context, "R", padding * 2 + barWidth, topInset, barWidth, barHeight, rmsR, _peakRHold);
    }

    private void DrawChannel(DrawingContext context, string label, double x, double y, double w, double h, float rms, float peak)
    {
        // Background
        var bg = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        context.FillRectangle(bg, new Rect(x, y, w, h));

        double rmsFrac = NormalizeDb(AmplitudeToDb(rms));
        double peakFrac = NormalizeDb(AmplitudeToDb(peak));

        double rmsHeight = rmsFrac * h;
        var rmsRect = new Rect(x, y + h - rmsHeight, w, rmsHeight);
        context.FillRectangle(PrimaryBrush, rmsRect);

        // Peak hold marker
        if (peakFrac > 0)
        {
            double peakY = y + h - peakFrac * h;
            var peakBrush = peak >= 0.99f ? Brushes.Red : SecondaryBrush;
            context.FillRectangle(peakBrush, new Rect(x, peakY - 1.5, w, 2.5));
        }

        IBrush textBrush = Foreground ?? Brushes.White;
        var formatted = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            textBrush);
        context.DrawText(formatted, new Point(x + w / 2 - formatted.Width / 2, y - formatted.Height - 1));
    }

    private static float DecayHold(float current, float instant, double elapsed)
    {
        if (instant > current) return instant;
        // decay ~6 dB/sec
        float decay = (float)(0.5 * elapsed);
        float v = current - decay;
        return v < instant ? instant : v;
    }

    private static float AmplitudeToDb(float amp)
    {
        if (amp <= 0) return MinDb;
        float db = 20f * MathF.Log10(amp);
        return db < MinDb ? MinDb : db;
    }

    private static double NormalizeDb(float db)
    {
        double t = (db - MinDb) / -MinDb;
        if (t < 0) return 0;
        if (t > 1) return 1;
        return t;
    }
}
