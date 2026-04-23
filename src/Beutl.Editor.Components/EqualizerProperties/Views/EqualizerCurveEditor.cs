using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Engine;

namespace Beutl.Editor.Components.EqualizerProperties.Views;

public sealed class EqualizerCurveEditor : Control
{
    public const float MinFrequency = 20f;
    public const float MaxFrequency = 20000f;
    public const float MinGain = -24f;
    public const float MaxGain = 24f;
    public const float MinQ = 0.1f;
    public const float MaxQ = 18f;
    private const double HandleRadius = 6.0;
    private const double HitTestRadius = 10.0;

    // The actual render sample rate is not known here; 48 kHz is the common session rate and
    // keeps the plot accurate enough across the audible range for user feedback.
    private const int ResponseSampleRate = 48000;

    public static readonly StyledProperty<IList<EqualizerBand>?> BandsProperty =
        AvaloniaProperty.Register<EqualizerCurveEditor, IList<EqualizerBand>?>(nameof(Bands));

    public static readonly StyledProperty<int> SelectedBandIndexProperty =
        AvaloniaProperty.Register<EqualizerCurveEditor, int>(nameof(SelectedBandIndex), defaultValue: -1,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<TimeSpan> CurrentTimeProperty =
        AvaloniaProperty.Register<EqualizerCurveEditor, TimeSpan>(nameof(CurrentTime));

    public static readonly RoutedEvent<EqualizerBandEventArgs> BandChangedEvent =
        RoutedEvent.Register<EqualizerCurveEditor, EqualizerBandEventArgs>(nameof(BandChanged), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<EqualizerBandEventArgs> BandConfirmedEvent =
        RoutedEvent.Register<EqualizerCurveEditor, EqualizerBandEventArgs>(nameof(BandConfirmed), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<EqualizerBandSelectedEventArgs> BandSelectedEvent =
        RoutedEvent.Register<EqualizerCurveEditor, EqualizerBandSelectedEventArgs>(nameof(BandSelected), RoutingStrategies.Bubble);

    private int _draggingIndex = -1;
    private float _dragStartFrequency;
    private float _dragStartGain;
    private Point _dragStartPoint;
    private int _hoverIndex = -1;
    private readonly List<Action> _bandSubscriptions = [];

    private DispatcherTimer? _wheelCommitTimer;
    private int _wheelBandIndex = -1;
    private EqualizerBand? _wheelBand;
    private float _wheelStartQ;

    static EqualizerCurveEditor()
    {
        AffectsRender<EqualizerCurveEditor>(BandsProperty, SelectedBandIndexProperty, CurrentTimeProperty);
        BandsProperty.Changed.AddClassHandler<EqualizerCurveEditor>((o, e) => o.OnBandsPropertyChanged(e));
    }

    public EqualizerCurveEditor()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    private void OnBandsPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyCollectionChanged oldNotify)
        {
            oldNotify.CollectionChanged -= OnBandsCollectionChanged;
        }
        // The Bands collection identity itself can change (e.g. band-count preset). A deferred
        // Q-wheel commit captured the old band index, so flush it against the old collection
        // before resubscribing so the commit lands on the right band.
        _wheelCommitTimer?.Stop();
        FlushWheelCommit();
        if (e.NewValue is INotifyCollectionChanged newNotify)
        {
            newNotify.CollectionChanged += OnBandsCollectionChanged;
        }
        ResubscribeBandProperties();
    }

    private void OnBandsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Preset changes rebuild the backing collection, which would otherwise let a deferred
        // Q-wheel commit look up a stale or out-of-range index.
        _wheelCommitTimer?.Stop();
        FlushWheelCommit();
        ResubscribeBandProperties();
        InvalidateVisual();
    }

    private void ResubscribeBandProperties()
    {
        foreach (var unsubscribe in _bandSubscriptions)
        {
            unsubscribe();
        }
        _bandSubscriptions.Clear();

        if (Bands is null) return;

        foreach (var band in Bands)
        {
            _bandSubscriptions.Add(SubscribeEdited(band.Frequency));
            _bandSubscriptions.Add(SubscribeEdited(band.Gain));
            _bandSubscriptions.Add(SubscribeEdited(band.Q));
            _bandSubscriptions.Add(SubscribeEdited(band.FilterType));
        }
    }

    private Action SubscribeEdited(INotifyEdited notifier)
    {
        EventHandler handler = (_, _) => InvalidateVisual();
        notifier.Edited += handler;
        return () => notifier.Edited -= handler;
    }

    public IList<EqualizerBand>? Bands
    {
        get => GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    public int SelectedBandIndex
    {
        get => GetValue(SelectedBandIndexProperty);
        set => SetValue(SelectedBandIndexProperty, value);
    }

    public TimeSpan CurrentTime
    {
        get => GetValue(CurrentTimeProperty);
        set => SetValue(CurrentTimeProperty, value);
    }

    public event EventHandler<EqualizerBandEventArgs>? BandChanged
    {
        add => AddHandler(BandChangedEvent, value);
        remove => RemoveHandler(BandChangedEvent, value);
    }

    public event EventHandler<EqualizerBandEventArgs>? BandConfirmed
    {
        add => AddHandler(BandConfirmedEvent, value);
        remove => RemoveHandler(BandConfirmedEvent, value);
    }

    public event EventHandler<EqualizerBandSelectedEventArgs>? BandSelected
    {
        add => AddHandler(BandSelectedEvent, value);
        remove => RemoveHandler(BandSelectedEvent, value);
    }

    public void NotifyBandsChanged() => InvalidateVisual();

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bands is null || Bands.Count == 0) return;

        // Starting a new drag should not silently absorb a pending wheel edit into the same
        // history transaction, so flush any deferred Q-wheel commit first.
        _wheelCommitTimer?.Stop();
        FlushWheelCommit();

        var point = e.GetPosition(this);
        int hit = HitTestHandle(point);
        if (hit >= 0)
        {
            _draggingIndex = hit;
            var band = Bands[hit];
            _dragStartFrequency = band.Frequency.CurrentValue;
            _dragStartGain = band.Gain.CurrentValue;
            _dragStartPoint = point;
            SelectedBandIndex = hit;
            RaiseEvent(new EqualizerBandSelectedEventArgs(BandSelectedEvent, hit));
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Bands is null) return;

        var point = e.GetPosition(this);

        if (_draggingIndex >= 0 && _draggingIndex < Bands.Count)
        {
            var band = Bands[_draggingIndex];
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
            double dx = point.X - _dragStartPoint.X;
            double dy = point.Y - _dragStartPoint.Y;

            bool onlyVertical = shift && Math.Abs(dy) >= Math.Abs(dx);
            bool onlyHorizontal = shift && Math.Abs(dx) > Math.Abs(dy);

            // Skip axes whose value is driven by an animation — writing CurrentValue would only
            // mutate the hidden base value, so the handle would snap back on the next render.
            if (!onlyHorizontal && IsGainAdjustable(band.FilterType.CurrentValue) && band.Gain.Animation is null)
            {
                float targetCompositeDb = GainFromY(point.Y);
                float freq = (float)GetEffectiveValue(band.Frequency);
                float othersContribution = CalculateResponseDbExcluding(freq, Bands, _draggingIndex);
                // At f0 the band's response equals gainDb for Peak but only gainDb/2 for shelves,
                // so invert the transfer so the cursor ends up under the handle after the edit.
                float responseRatio = GainToResponseRatioAtF0(band.FilterType.CurrentValue);
                float newGain = Math.Clamp((targetCompositeDb - othersContribution) / responseRatio, MinGain, MaxGain);
                float oldGain = band.Gain.CurrentValue;
                if (!AreEqual(oldGain, newGain))
                {
                    band.Gain.CurrentValue = newGain;
                    RaiseEvent(new EqualizerBandEventArgs(BandChangedEvent, _draggingIndex, EqualizerBandProperty.Gain, oldGain, newGain));
                }
            }

            if (!onlyVertical && band.Frequency.Animation is null)
            {
                float newFreq = FrequencyFromX(point.X);
                float oldFreq = band.Frequency.CurrentValue;
                if (!AreEqual(oldFreq, newFreq))
                {
                    band.Frequency.CurrentValue = newFreq;
                    RaiseEvent(new EqualizerBandEventArgs(BandChangedEvent, _draggingIndex, EqualizerBandProperty.Frequency, oldFreq, newFreq));
                }
            }

            InvalidateVisual();
        }
        else
        {
            int newHover = HitTestHandle(point);
            if (newHover != _hoverIndex)
            {
                _hoverIndex = newHover;
                InvalidateVisual();
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggingIndex >= 0 && Bands is not null && _draggingIndex < Bands.Count)
        {
            var band = Bands[_draggingIndex];
            float newGain = band.Gain.CurrentValue;
            float newFreq = band.Frequency.CurrentValue;

            if (!AreEqual(newGain, _dragStartGain))
            {
                RaiseEvent(new EqualizerBandEventArgs(BandConfirmedEvent, _draggingIndex, EqualizerBandProperty.Gain, _dragStartGain, newGain));
            }
            if (!AreEqual(newFreq, _dragStartFrequency))
            {
                RaiseEvent(new EqualizerBandEventArgs(BandConfirmedEvent, _draggingIndex, EqualizerBandProperty.Frequency, _dragStartFrequency, newFreq));
            }
        }

        _draggingIndex = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Bands is null) return;

        var point = e.GetPosition(this);
        int hit = HitTestHandle(point);
        if (hit < 0) return;

        var band = Bands[hit];
        // Skip the scroll edit when Q is animated — we would only move the hidden base value.
        if (band.Q.Animation is not null) return;
        // Touchpad horizontal scrolls can deliver Delta.Y == 0; without this guard those gestures
        // would fall through to the else branch and silently decrease Q.
        if (e.Delta.Y == 0) return;
        float oldQ = band.Q.CurrentValue;
        double factor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
        float newQ = Math.Clamp((float)(oldQ * factor), MinQ, MaxQ);
        if (!AreEqual(oldQ, newQ))
        {
            if (_wheelBandIndex != hit)
            {
                FlushWheelCommit();
                _wheelBandIndex = hit;
                _wheelBand = band;
                _wheelStartQ = oldQ;
            }

            band.Q.CurrentValue = newQ;
            RaiseEvent(new EqualizerBandEventArgs(BandChangedEvent, hit, EqualizerBandProperty.Q, oldQ, newQ));
            InvalidateVisual();

            RestartWheelTimer();
        }
        e.Handled = true;
    }

    private void RestartWheelTimer()
    {
        if (_wheelCommitTimer is null)
        {
            _wheelCommitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _wheelCommitTimer.Tick += OnWheelCommitTick;
        }
        _wheelCommitTimer.Stop();
        _wheelCommitTimer.Start();
    }

    private void OnWheelCommitTick(object? sender, EventArgs e)
    {
        _wheelCommitTimer?.Stop();
        FlushWheelCommit();
    }

    private void FlushWheelCommit()
    {
        var band = _wheelBand;
        int index = _wheelBandIndex;
        _wheelBand = null;
        _wheelBandIndex = -1;
        // Look up the commit against the captured band reference rather than by index so a
        // preset rebuild that changes the Bands collection cannot drop or misattribute the edit.
        if (band is null) return;

        float newQ = band.Q.CurrentValue;
        if (!AreEqual(_wheelStartQ, newQ))
        {
            RaiseEvent(new EqualizerBandEventArgs(BandConfirmedEvent, index, EqualizerBandProperty.Q, _wheelStartQ, newQ));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // The deferred Q-wheel commit relies on a 400 ms timer. If the editor is torn down before
        // that tick fires (selection change, pane closed, tab switched), flush synchronously so
        // the mutation is finalized instead of leaking into the next unrelated history entry.
        _wheelCommitTimer?.Stop();
        FlushWheelCommit();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        // When the pointer leaves the curve editor the next action is almost certainly outside our
        // control (e.g. changing the band-count preset). Commit any pending wheel edit now so it
        // lands in its own history transaction rather than merging with the unrelated next edit.
        _wheelCommitTimer?.Stop();
        FlushWheelCommit();
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var bgBrush = new SolidColorBrush(Color.FromArgb(32, 128, 128, 128));
        context.FillRectangle(bgBrush, new Rect(0, 0, w, h));

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(64, 128, 128, 128)), 1);
        var gridStrongPen = new Pen(new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)), 1);

        // dB gridlines
        for (int db = -24; db <= 24; db += 6)
        {
            double y = YFromGain(db);
            context.DrawLine(db == 0 ? gridStrongPen : gridPen, new Point(0, y), new Point(w, y));
        }

        // Frequency gridlines (decades and 1/3 octaves)
        foreach (double freq in new double[] { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            double x = XFromFrequency((float)freq);
            bool isDecade = Math.Abs(Math.Log10(freq) - Math.Round(Math.Log10(freq))) < 0.01;
            context.DrawLine(isDecade ? gridStrongPen : gridPen, new Point(x, 0), new Point(x, h));
        }

        // Response curve
        if (Bands is { Count: > 0 } bands)
        {
            const int samples = 256;
            var points = new Point[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / (samples - 1);
                float freq = (float)(MinFrequency * Math.Pow(MaxFrequency / MinFrequency, t));
                float db = CalculateResponseDb(freq, bands);
                double x = XFromFrequency(freq);
                double y = YFromGain(db);
                points[i] = new Point(x, y);
            }

            var curvePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 86, 156, 214)), 2);
            for (int i = 1; i < samples; i++)
            {
                context.DrawLine(curvePen, points[i - 1], points[i]);
            }

            // Handles — place on the composite response curve
            for (int i = 0; i < bands.Count; i++)
            {
                var band = bands[i];
                if (!band.IsEnabled) continue;

                float freq = (float)GetEffectiveValue(band.Frequency);
                double x = XFromFrequency(freq);
                double y = YFromGain(CalculateResponseDb(freq, bands));
                bool isSelected = i == SelectedBandIndex;
                bool isHover = i == _hoverIndex || i == _draggingIndex;
                double radius = isSelected || isHover ? HandleRadius + 2 : HandleRadius;

                var fillColor = isSelected
                    ? Color.FromArgb(255, 86, 156, 214)
                    : Color.FromArgb(200, 200, 200, 200);
                var strokeColor = Color.FromArgb(255, 32, 32, 32);

                context.DrawEllipse(new SolidColorBrush(fillColor), new Pen(new SolidColorBrush(strokeColor), 1.5),
                    new Point(x, y), radius, radius);
            }
        }
    }

    private int HitTestHandle(Point point)
    {
        if (Bands is null) return -1;
        for (int i = 0; i < Bands.Count; i++)
        {
            var band = Bands[i];
            if (!band.IsEnabled) continue;

            float freq = (float)GetEffectiveValue(band.Frequency);
            double x = XFromFrequency(freq);
            double y = YFromGain(CalculateResponseDb(freq, Bands));
            double dx = point.X - x;
            double dy = point.Y - y;
            if (dx * dx + dy * dy <= HitTestRadius * HitTestRadius)
            {
                return i;
            }
        }
        return -1;
    }

    private float GetEffectiveValue(IProperty<float> property)
    {
        if (property.Animation is IAnimation<float> animation)
        {
            return animation.GetAnimatedValue(CurrentTime);
        }
        return property.CurrentValue;
    }

    private double XFromFrequency(float frequency)
    {
        double t = (Math.Log10(Math.Clamp(frequency, MinFrequency, MaxFrequency)) - Math.Log10(MinFrequency)) /
                   (Math.Log10(MaxFrequency) - Math.Log10(MinFrequency));
        return t * Bounds.Width;
    }

    private float FrequencyFromX(double x)
    {
        double t = Math.Clamp(x / Bounds.Width, 0, 1);
        double f = Math.Pow(10, Math.Log10(MinFrequency) + t * (Math.Log10(MaxFrequency) - Math.Log10(MinFrequency)));
        return (float)Math.Clamp(f, MinFrequency, MaxFrequency);
    }

    private double YFromGain(float gain)
    {
        double t = 1.0 - (Math.Clamp(gain, MinGain, MaxGain) - MinGain) / (MaxGain - MinGain);
        return t * Bounds.Height;
    }

    private float GainFromY(double y)
    {
        double t = 1.0 - Math.Clamp(y / Bounds.Height, 0, 1);
        return (float)Math.Clamp(MinGain + t * (MaxGain - MinGain), MinGain, MaxGain);
    }

    private float CalculateResponseDb(float frequency, IList<EqualizerBand> bands)
    {
        double total = 0.0;
        for (int i = 0; i < bands.Count; i++)
        {
            var band = bands[i];
            if (!band.IsEnabled) continue;
            total += CalculateBandResponseDb(frequency, band);
        }
        return (float)total;
    }

    private float CalculateResponseDbExcluding(float frequency, IList<EqualizerBand> bands, int excludeIndex)
    {
        double total = 0.0;
        for (int i = 0; i < bands.Count; i++)
        {
            if (i == excludeIndex) continue;
            var band = bands[i];
            if (!band.IsEnabled) continue;
            total += CalculateBandResponseDb(frequency, band);
        }
        return (float)total;
    }

    private static bool IsGainAdjustable(BiQuadFilterType type) => type switch
    {
        BiQuadFilterType.Peak or BiQuadFilterType.LowShelf or BiQuadFilterType.HighShelf => true,
        _ => false
    };

    private static float GainToResponseRatioAtF0(BiQuadFilterType type) => type switch
    {
        BiQuadFilterType.LowShelf or BiQuadFilterType.HighShelf => 0.5f,
        _ => 1.0f,
    };

    private double CalculateBandResponseDb(float frequency, EqualizerBand band)
    {
        float f0 = (float)GetEffectiveValue(band.Frequency);
        float q = (float)Math.Max(GetEffectiveValue(band.Q), MinQ);
        float gain = (float)GetEffectiveValue(band.Gain);
        return BiQuadFilter.CalculateResponseDb(
            band.FilterType.CurrentValue, f0, q, gain, ResponseSampleRate, frequency);
    }

    private static bool AreEqual(float a, float b) => Math.Abs(a - b) < 1e-5f;
}
