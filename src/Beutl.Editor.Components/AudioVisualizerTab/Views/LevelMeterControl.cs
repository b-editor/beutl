using Avalonia;
using Avalonia.Media;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Editor.Components.AudioVisualizerTab.Utilities;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public sealed class LevelMeterControl : AudioVisualizerControlBase
{
    private const int RmsWindowSamples = 1024;
    private const float MinDb = -60f;
    private const double ScaleAreaWidth = 20.0;
    private const double BarPadding = 4.0;
    private const double LabelFontSize = 12.0;

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
    private bool _clipLHeld;
    private bool _clipRHeld;
    private float _displayLufs = -160f;
    private bool _isSettling;

    private readonly LoudnessMeter _loudness = new();
    private float[] _loudnessLeft = [];
    private float[] _loudnessRight = [];

    // Surround (3+ channel) state, sized to the buffer's channel count.
    private AudioChannelLayout _surroundLayout;
    private float[][] _meterChannels = [];
    private float[][] _loudnessChannels = [];
    private float[] _channelRms = [];
    private float[] _channelPeaks = [];
    private float[] _peakHolds = [];
    private float[] _truePeakHolds = [];
    private DateTime?[] _clipAts = [];
    private bool[] _clipsHeld = [];

    private readonly record struct BarMetrics(double ScaleWidth, double ClipLedHeight, double ClipLedY, double TopInset, double BarHeight);

    // A hold is still falling, the loudness digit easing or a clip light lit.
    protected internal override bool IsSettling => _isSettling;

    public override void Render(DrawingContext context)
    {
        _isSettling = false;
        Rect bounds = new(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);

        AudioSampleRingBuffer? buffer = RingBuffer;
        if (buffer == null || bounds.Width < 20 || bounds.Height < 20) return;

        int channelCount = buffer.ChannelCount;
        if (channelCount > 2)
        {
            RenderSurround(context, bounds, buffer, channelCount);
            return;
        }

        var (rmsL, rmsR, peakL, peakR) = buffer.ComputeMeters(RmsWindowSamples, PlayheadTime);
        var (now, elapsed) = AdvanceClock();

        _peakLHold = DecayHold(_peakLHold, peakL, elapsed);
        _peakRHold = DecayHold(_peakRHold, peakR, elapsed);

        // Samples holding a light on can stop the drawing for any length of time, so its hold starts when they let go.
        if (peakL >= ClipThreshold || _clipLHeld) _clipLAt = now;
        if (peakR >= ClipThreshold || _clipRHeld) _clipRAt = now;
        _clipLHeld = peakL >= ClipThreshold;
        _clipRHeld = peakR >= ClipThreshold;
        bool clipL = _clipLAt is { } cl && now - cl < s_clipHold;
        bool clipR = _clipRAt is { } cr && now - cr < s_clipHold;
        // A light the samples on screen hold on changes nothing until they do. One they have let go of is waiting to go out.
        _isSettling |= (clipL && !_clipLHeld) || (clipR && !_clipRHeld);

        UpdateLoudnessAndTruePeak(buffer, elapsed);

        var (scaleWidth, clipLedHeight, clipLedY, topInset, barHeight) = MeasureBars(bounds);
        double barAreaWidth = bounds.Width - BarPadding * 3 - scaleWidth;
        double barWidth = barAreaWidth / 2;

        double leftX = BarPadding;
        double rightX = BarPadding * 2 + barWidth;
        double scaleX = BarPadding * 3 + barWidth * 2;

        DrawChannelLabel(context, "L", leftX + barWidth / 2, 0);
        DrawChannelLabel(context, "R", rightX + barWidth / 2, 0);

        DrawClipLed(context, leftX, clipLedY, barWidth, clipLedHeight, clipL);
        DrawClipLed(context, rightX, clipLedY, barWidth, clipLedHeight, clipR);

        DrawChannel(context, leftX, topInset, barWidth, barHeight, rmsL, _peakLHold);
        DrawChannel(context, rightX, topInset, barWidth, barHeight, rmsR, _peakRHold);
        if (scaleWidth > 0)
        {
            DrawDbScale(context, scaleX, topInset, scaleWidth, barHeight, leftX, leftX + barWidth * 2 + BarPadding);
        }

        DrawLoudnessAndTruePeakText(context, leftX, topInset + barHeight + 4);
    }

    // One bar per source channel with its speaker label, and BS.1770 weighted
    // loudness over all of them. The stereo layout above stays as it was.
    private void RenderSurround(DrawingContext context, Rect bounds, AudioSampleRingBuffer buffer, int channelCount)
    {
        EnsureSurroundState(channelCount);
        int got = buffer.ComputeChannelMeters(RmsWindowSamples, PlayheadTime, _meterChannels, _channelRms, _channelPeaks);
        if (got == 0)
        {
            Array.Clear(_channelRms);
            Array.Clear(_channelPeaks);
        }

        var (now, elapsed) = AdvanceClock();

        for (int ch = 0; ch < channelCount; ch++)
        {
            _peakHolds[ch] = DecayHold(_peakHolds[ch], _channelPeaks[ch], elapsed);
            if (_channelPeaks[ch] >= ClipThreshold || _clipsHeld[ch]) _clipAts[ch] = now;
            _clipsHeld[ch] = _channelPeaks[ch] >= ClipThreshold;
        }

        UpdateSurroundLoudnessAndTruePeak(buffer, channelCount, elapsed);

        double gap = 2;
        var (scaleWidth, clipLedHeight, clipLedY, topInset, barHeight) = MeasureBars(bounds);
        double barAreaWidth = bounds.Width - BarPadding * 3 - scaleWidth - gap * (channelCount - 1);
        double barWidth = Math.Max(1, barAreaWidth / channelCount);

        for (int ch = 0; ch < channelCount; ch++)
        {
            double x = BarPadding + ch * (barWidth + gap);
            bool clip = _clipAts[ch] is { } at && now - at < s_clipHold;
            _isSettling |= clip && !_clipsHeld[ch];
            DrawChannelLabel(context, AudioChannelMap.GetChannelName(_surroundLayout, ch), x + barWidth / 2, 0);
            DrawClipLed(context, x, clipLedY, barWidth, clipLedHeight, clip);
            DrawChannel(context, x, topInset, barWidth, barHeight, _channelRms[ch], _peakHolds[ch]);
        }

        double barsEnd = BarPadding + channelCount * barWidth + (channelCount - 1) * gap;
        if (scaleWidth > 0)
        {
            DrawDbScale(context, barsEnd + BarPadding, topInset, scaleWidth, barHeight, BarPadding, barsEnd);
        }

        DrawSurroundLoudnessAndTruePeakText(context, BarPadding, topInset + barHeight + 4);
    }

    private (DateTime Now, double Elapsed) AdvanceClock()
    {
        DateTime now = DateTime.UtcNow;
        double elapsed = Math.Clamp((now - _lastRenderTime).TotalSeconds, 0, 0.5);
        _lastRenderTime = now;
        return (now, elapsed);
    }

    private BarMetrics MeasureBars(Rect bounds)
    {
        double scaleWidth = bounds.Width >= 80 ? ScaleAreaWidth : 0;
        double clipLedHeight = 8;
        // Lay out top region: [channel label][gap][clip LED][gap][bar].
        // Use a real measurement for the channel label so larger fonts don't
        // overflow into the LED.
        double labelHeight = MeasureTextHeight("L", LabelFontSize);
        double labelGap = 2;
        double ledGap = 4;
        double textStripHeight = labelHeight * 2;
        double topInset = labelHeight + labelGap + clipLedHeight + ledGap;
        double barHeight = bounds.Height - topInset - BarPadding - textStripHeight;
        return new BarMetrics(scaleWidth, clipLedHeight, labelHeight + labelGap, topInset, barHeight);
    }

    private void EnsureSurroundState(int channelCount)
    {
        if (_meterChannels.Length == channelCount) return;
        _surroundLayout = AudioChannelLayout.GetDefault(channelCount);
        _meterChannels = new float[channelCount][];
        for (int ch = 0; ch < channelCount; ch++) _meterChannels[ch] = new float[RmsWindowSamples];
        _loudnessChannels = new float[channelCount][];
        for (int ch = 0; ch < channelCount; ch++) _loudnessChannels[ch] = [];
        _channelRms = new float[channelCount];
        _channelPeaks = new float[channelCount];
        _peakHolds = new float[channelCount];
        _truePeakHolds = new float[channelCount];
        _clipAts = new DateTime?[channelCount];
        _clipsHeld = new bool[channelCount];
    }

    private void UpdateSurroundLoudnessAndTruePeak(AudioSampleRingBuffer buffer, int channelCount, double elapsed)
    {
        int sampleRate = buffer.SampleRate;
        if (sampleRate <= 0) return;

        int n = (int)(LoudnessWindowSeconds * sampleRate);
        if (_loudnessChannels[0].Length < n)
        {
            for (int ch = 0; ch < channelCount; ch++) _loudnessChannels[ch] = new float[n];
        }

        int got = buffer.ReadChannelsAroundTime(PlayheadTime, _loudnessChannels, n, out int leadingZeros);
        if (got <= 0) return;

        EaseDisplayLufs(_loudness.Compute(_loudnessChannels, leadingZeros, got, _surroundLayout, sampleRate), elapsed);

        for (int ch = 0; ch < channelCount; ch++)
        {
            float tp = TruePeakDetector.Detect(_loudnessChannels[ch].AsSpan(leadingZeros, got));
            _truePeakHolds[ch] = DecayHold(_truePeakHolds[ch], tp, elapsed);
        }
    }

    private void DrawSurroundLoudnessAndTruePeakText(DrawingContext context, double x, double y)
    {
        // Too many channels to list each true peak; show the loudest and where it is.
        int loudest = 0;
        for (int ch = 1; ch < _truePeakHolds.Length; ch++)
        {
            if (_truePeakHolds[ch] > _truePeakHolds[loudest]) loudest = ch;
        }

        DrawReadout(context, x, y, $"TP {LinearToDbtp(_truePeakHolds[loudest]),5:0.0} ({AudioChannelMap.GetChannelName(_surroundLayout, loudest)})");
    }

    private double MeasureTextHeight(string text, double fontSize)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            fontSize,
            Brushes.White);
        return formatted.Height;
    }

    private void DrawChannelLabel(DrawingContext context, string label, double centerX, double topY)
    {
        var formatted = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            LabelFontSize,
            Brushes.White);
        context.DrawText(formatted, new Point(centerX - formatted.Width / 2, topY));
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

        int got = buffer.ReadAroundTime(PlayheadTime, _loudnessLeft, _loudnessRight, n, out int leadingZeros);
        if (got <= 0) return;

        // Exclude the zero-padded prefix from LUFS/true-peak analysis — otherwise
        // the silent samples dilute the mean square and pull the reading toward -∞.
        ReadOnlySpan<float> l = _loudnessLeft.AsSpan(leadingZeros, got);
        ReadOnlySpan<float> r = _loudnessRight.AsSpan(leadingZeros, got);

        EaseDisplayLufs(_loudness.Compute(l, r, sampleRate), elapsed);

        float tpL = TruePeakDetector.Detect(l);
        float tpR = TruePeakDetector.Detect(r);
        _truePeakLHold = DecayHold(_truePeakLHold, tpL, elapsed);
        _truePeakRHold = DecayHold(_truePeakRHold, tpR, elapsed);
    }

    // Light low-pass on the displayed value so the digit isn't twitchy.
    private void EaseDisplayLufs(float lufs, double elapsed)
    {
        if (float.IsFinite(lufs))
        {
            float alpha = (float)Math.Clamp(elapsed / 0.2, 0.05, 1.0);
            _displayLufs = _displayLufs * (1f - alpha) + lufs * alpha;
            // The digit shows one decimal place.
            _isSettling |= MathF.Abs(_displayLufs - lufs) > 0.05f;
        }
    }

    private void DrawLoudnessAndTruePeakText(DrawingContext context, double x, double y)
        => DrawReadout(context, x, y, $"TP L {LinearToDbtp(_truePeakLHold),5:0.0}  R {LinearToDbtp(_truePeakRHold),5:0.0}");

    private void DrawReadout(DrawingContext context, double x, double y, string truePeakLabel)
    {
        Typeface tf = Typeface.Default;

        string lufsLabel = _displayLufs <= -159f
            ? "M  -∞ LUFS"
            : $"M {_displayLufs,5:0.0} LUFS";

        var lufsText = new FormattedText(lufsLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, LabelFontSize, Brushes.White);
        var tpText = new FormattedText(truePeakLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, LabelFontSize, Brushes.White);

        context.DrawText(lufsText, new Point(x, y));
        context.DrawText(tpText, new Point(x, y + lufsText.Height));
    }

    private static float LinearToDbtp(float linear)
    {
        if (linear <= 0f) return -60f;
        return AudioMath.ConvertLinearToDb(linear);
    }

    private void DrawChannel(DrawingContext context, double x, double y, double w, double h, float rms, float peak)
    {
        var bg = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        context.FillRectangle(bg, new Rect(x, y, w, h));

        double rmsFrac = NormalizeDb(AmplitudeToDb(rms));
        double peakFrac = NormalizeDb(AmplitudeToDb(peak));

        double rmsHeight = rmsFrac * h;
        var rmsRect = new Rect(x, y + h - rmsHeight, w, rmsHeight);
        context.FillRectangle(PrimaryBrush, rmsRect);

        if (peakFrac > 0)
        {
            double peakY = y + h - peakFrac * h;
            var peakBrush = peak >= 0.99f ? Brushes.Red : SecondaryBrush;
            context.FillRectangle(peakBrush, new Rect(x, peakY - 1.5, w, 2.5));
        }
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
        var tickPen = new Pen(new SolidColorBrush(Colors.Gray, 0.4), 0.5);

        foreach (float db in s_dbTicks)
        {
            double frac = NormalizeDb(db);
            double tickY = y + h - frac * h;

            context.DrawLine(tickPen, new Point(tickStart, tickY), new Point(tickEnd - 2, tickY));

            string label = db.ToString("0", CultureInfo.InvariantCulture);
            var formatted = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                LabelFontSize,
                Brushes.LightGray);
            // Center label on tick when possible; at edges anchor the label so its
            // top (top tick) or bottom (bottom tick) sits on the tick line —
            // otherwise the label appears visibly offset from its tick.
            double labelY = tickY - formatted.Height / 2;
            if (labelY < y) labelY = tickY;
            else if (labelY + formatted.Height > y + h) labelY = tickY - formatted.Height;
            context.DrawText(formatted, new Point(x + w - formatted.Width - 1, labelY));
        }
    }

    private float DecayHold(float current, float instant, double elapsed)
    {
        if (instant > current) return instant;
        // Lowers the amplitude linearly by 0.5 per second.
        float decay = (float)(0.5 * elapsed);
        float v = current - decay;
        if (v < instant) return instant;

        _isSettling = true;
        return v;
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
