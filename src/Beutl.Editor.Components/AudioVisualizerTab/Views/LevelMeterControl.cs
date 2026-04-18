using Avalonia;
using Avalonia.Media;
using Beutl.Audio.Graph;
using Beutl.Editor.Components.AudioVisualizerTab.Utilities;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public sealed class LevelMeterControl : AudioVisualizerControlBase
{
    private const int RmsWindowSamples = 1024;
    private const float MinDb = -60f;
    private const double ScaleAreaWidth = 26.0;

    // 0 dBFS in float audio = ±1.0. Use slightly below 1.0 as the trigger so that
    // values that round-trip through resampling/limiting still register.
    private const float ClipThreshold = 0.999f;
    private static readonly TimeSpan s_clipHold = TimeSpan.FromSeconds(2.0);
    private const double LoudnessWindowSeconds = 0.4;

    private static readonly float[] s_dbTicks = [0f, -3f, -6f, -12f, -24f, -36f, -48f, -60f];

    private float _peakLHold;
    private float _peakRHold;
    private float _truePeakLHold;
    private float _truePeakRHold;
    private DateTime _lastRenderTime = DateTime.UtcNow;
    private DateTime? _clipLAt;
    private DateTime? _clipRAt;
    private float _displayLufs = -160f;

    private readonly LoudnessMeter _loudness = new();
    private float[] _loudnessLeft = [];
    private float[] _loudnessRight = [];

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

        if (peakL >= ClipThreshold) _clipLAt = now;
        if (peakR >= ClipThreshold) _clipRAt = now;
        bool clipL = _clipLAt is { } cl && now - cl < s_clipHold;
        bool clipR = _clipRAt is { } cr && now - cr < s_clipHold;

        UpdateLoudnessAndTruePeak(buffer, elapsed);

        double padding = 4;
        double scaleWidth = bounds.Width >= 80 ? ScaleAreaWidth : 0;
        double textStripHeight = 28;
        double barAreaWidth = bounds.Width - padding * 3 - scaleWidth;
        double barWidth = barAreaWidth / 2;
        double clipLedHeight = 6;
        double topInset = 10 + clipLedHeight + 2;
        double barHeight = bounds.Height - topInset - padding - textStripHeight;

        double leftX = padding;
        double rightX = padding * 2 + barWidth;
        double scaleX = padding * 3 + barWidth * 2;

        DrawClipLed(context, leftX, topInset - clipLedHeight - 2, barWidth, clipLedHeight, clipL);
        DrawClipLed(context, rightX, topInset - clipLedHeight - 2, barWidth, clipLedHeight, clipR);

        DrawChannel(context, "L", leftX, topInset, barWidth, barHeight, rmsL, _peakLHold);
        DrawChannel(context, "R", rightX, topInset, barWidth, barHeight, rmsR, _peakRHold);
        if (scaleWidth > 0)
        {
            DrawDbScale(context, scaleX, topInset, scaleWidth, barHeight, leftX, leftX + barWidth * 2 + padding);
        }

        DrawLoudnessAndTruePeakText(context, leftX, topInset + barHeight + 2, leftX + barWidth * 2 + padding);
    }

    private void UpdateLoudnessAndTruePeak(AudioSampleRingBuffer buffer, double elapsed)
    {
        int sampleRate = buffer.SampleRate;
        if (sampleRate <= 0) return;

        int n = (int)(LoudnessWindowSeconds * sampleRate);
        if (_loudnessLeft.Length < n)
        {
            _loudnessLeft = new float[n];
            _loudnessRight = new float[n];
        }

        int got = buffer.ReadAroundTime(PlayheadTime, _loudnessLeft, _loudnessRight, n);
        if (got <= 0) return;

        ReadOnlySpan<float> l = _loudnessLeft.AsSpan(0, n);
        ReadOnlySpan<float> r = _loudnessRight.AsSpan(0, n);

        float lufs = _loudness.Compute(l, r, sampleRate);
        // Light low-pass on the displayed value so the digit isn't twitchy.
        if (float.IsFinite(lufs))
        {
            float alpha = (float)Math.Clamp(elapsed / 0.2, 0.05, 1.0);
            _displayLufs = _displayLufs * (1f - alpha) + lufs * alpha;
        }

        float tpL = TruePeakDetector.Detect(l);
        float tpR = TruePeakDetector.Detect(r);
        _truePeakLHold = DecayHold(_truePeakLHold, tpL, elapsed);
        _truePeakRHold = DecayHold(_truePeakRHold, tpR, elapsed);
    }

    private void DrawLoudnessAndTruePeakText(DrawingContext context, double x, double y, double right)
    {
        IBrush brush = Foreground ?? Brushes.White;
        Typeface tf = Typeface.Default;

        string lufsLabel = _displayLufs <= -159f
            ? "M  -∞ LUFS"
            : $"M {_displayLufs,5:0.0} LUFS";
        string tpLabel = $"TP L {LinearToDbtp(_truePeakLHold),5:0.0}  R {LinearToDbtp(_truePeakRHold),5:0.0}";

        var lufsText = new FormattedText(lufsLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, brush);
        var tpText = new FormattedText(tpLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, brush);

        context.DrawText(lufsText, new Point(x, y));
        context.DrawText(tpText, new Point(x, y + lufsText.Height));
        _ = right;
    }

    private static float LinearToDbtp(float linear)
    {
        if (linear <= 0f) return -60f;
        return AudioMath.ConvertLinearToDb(linear);
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
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            10,
            textBrush);
        context.DrawText(formatted, new Point(x + w / 2 - formatted.Width / 2, y - formatted.Height - 1));
    }

    private static void DrawClipLed(DrawingContext context, double x, double y, double w, double h, bool active)
    {
        IBrush bg = active
            ? Brushes.Red
            : new SolidColorBrush(Color.FromArgb(60, 255, 80, 80));
        context.FillRectangle(bg, new Rect(x, y, w, h));
    }

    private void DrawDbScale(DrawingContext context, double x, double y, double w, double h, double tickStart, double tickEnd)
    {
        IBrush textBrush = Foreground ?? Brushes.LightGray;
        var tickPen = new Pen(new SolidColorBrush(Colors.Gray, 0.4), 0.5);

        foreach (float db in s_dbTicks)
        {
            double frac = NormalizeDb(db);
            double tickY = y + h - frac * h;

            context.DrawLine(tickPen, new Point(tickStart, tickY), new Point(tickEnd - 2, tickY));

            string label = db == 0f ? "0" : db.ToString("0", CultureInfo.InvariantCulture);
            var formatted = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                9,
                textBrush);
            double labelY = Math.Clamp(tickY - formatted.Height / 2, y, y + h - formatted.Height);
            context.DrawText(formatted, new Point(x + w - formatted.Width - 1, labelY));
        }
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
        // AudioMath.ConvertLinearToDb floors at -100 dB; clamp to the meter's display floor.
        float db = AudioMath.ConvertLinearToDb(amp);
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
