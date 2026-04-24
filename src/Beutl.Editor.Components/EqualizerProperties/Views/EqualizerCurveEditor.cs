using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Beutl.Audio.Effects.Equalizer;
using Beutl.Editor.Components.EqualizerProperties.ViewModels;

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

    // Used only when the host has not bound a session sample rate (e.g. design-time).
    private const int DefaultResponseSampleRate = 48000;

    private static readonly ImmutableSolidColorBrush s_backgroundBrush = new(Color.FromArgb(32, 128, 128, 128));
    private static readonly ImmutablePen s_gridPen = new(new ImmutableSolidColorBrush(Color.FromArgb(64, 128, 128, 128)));
    private static readonly ImmutablePen s_gridStrongPen = new(new ImmutableSolidColorBrush(Color.FromArgb(120, 128, 128, 128)));
    private static readonly ImmutablePen s_curvePen = new(new ImmutableSolidColorBrush(Color.FromArgb(220, 86, 156, 214)), 2);
    private static readonly ImmutableSolidColorBrush s_handleSelectedBrush = new(Color.FromArgb(255, 86, 156, 214));
    private static readonly ImmutableSolidColorBrush s_handleNormalBrush = new(Color.FromArgb(200, 200, 200, 200));
    private static readonly ImmutablePen s_handleStrokePen = new(new ImmutableSolidColorBrush(Color.FromArgb(255, 32, 32, 32)), 1.5);

    public static readonly StyledProperty<ObservableCollection<EqualizerBandItemViewModel>?> BandViewModelsProperty =
        AvaloniaProperty.Register<EqualizerCurveEditor, ObservableCollection<EqualizerBandItemViewModel>?>(nameof(BandViewModels));

    public static readonly StyledProperty<int> SelectedBandIndexProperty =
        AvaloniaProperty.Register<EqualizerCurveEditor, int>(nameof(SelectedBandIndex), defaultValue: -1,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<TimeSpan> CurrentTimeProperty =
        AvaloniaProperty.Register<EqualizerCurveEditor, TimeSpan>(nameof(CurrentTime));

    public static readonly StyledProperty<int> SampleRateProperty =
        AvaloniaProperty.Register<EqualizerCurveEditor, int>(nameof(SampleRate), defaultValue: DefaultResponseSampleRate);

    private int _draggingIndex = -1;
    private float _dragStartFrequency;
    private float _dragStartGain;
    private Point _dragStartPoint;
    private int _hoverIndex = -1;
    private readonly List<Action> _bandSubscriptions = [];

    private DispatcherTimer? _wheelCommitTimer;
    private int _wheelBandIndex = -1;
    private EqualizerBandItemViewModel? _wheelBandVm;
    private float _wheelStartQ;

    private ObservableCollection<EqualizerBandItemViewModel>? _bandViewModels;

    static EqualizerCurveEditor()
    {
        AffectsRender<EqualizerCurveEditor>(BandViewModelsProperty, SelectedBandIndexProperty, CurrentTimeProperty, SampleRateProperty);
        BandViewModelsProperty.Changed.AddClassHandler<EqualizerCurveEditor>((o, e) => o.OnBandViewModelsChanged(
            e.OldValue as ObservableCollection<EqualizerBandItemViewModel>,
            e.NewValue as ObservableCollection<EqualizerBandItemViewModel>));
    }

    public EqualizerCurveEditor()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public ObservableCollection<EqualizerBandItemViewModel>? BandViewModels
    {
        get => GetValue(BandViewModelsProperty);
        set => SetValue(BandViewModelsProperty, value);
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

    public int SampleRate
    {
        get => GetValue(SampleRateProperty);
        set => SetValue(SampleRateProperty, value);
    }

    private int EffectiveSampleRate => SampleRate > 0 ? SampleRate : DefaultResponseSampleRate;

    private float EffectiveMaxFrequency => Math.Min(MaxFrequency, EffectiveSampleRate / 2f - 1f);

    private void OnBandViewModelsChanged(
        ObservableCollection<EqualizerBandItemViewModel>? oldValue,
        ObservableCollection<EqualizerBandItemViewModel>? newValue)
    {
        if (oldValue is not null)
            oldValue.CollectionChanged -= OnBandsCollectionChanged;

        _wheelCommitTimer?.Stop();
        FlushWheelCommit();

        _bandViewModels = newValue;

        if (newValue is not null)
            newValue.CollectionChanged += OnBandsCollectionChanged;

        ResubscribeBandProperties();
    }

    private void OnBandsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
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

        if (_bandViewModels is null) return;

        foreach (var vm in _bandViewModels)
        {
            var band = vm.Band;
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_bandViewModels is not { Count: > 0 }) return;

        _wheelCommitTimer?.Stop();
        FlushWheelCommit();

        var point = e.GetPosition(this);
        int hit = HitTestHandle(point);
        if (hit >= 0)
        {
            _draggingIndex = hit;
            var vm = _bandViewModels[hit];
            _dragStartFrequency = vm.GetEffectiveValue(vm.Band.Frequency, CurrentTime);
            _dragStartGain = vm.GetEffectiveValue(vm.Band.Gain, CurrentTime);
            _dragStartPoint = point;
            SelectedBandIndex = hit;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_bandViewModels is null) return;

        var point = e.GetPosition(this);

        if (_draggingIndex >= 0 && _draggingIndex < _bandViewModels.Count)
        {
            var vm = _bandViewModels[_draggingIndex];
            var band = vm.Band;
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
            double dx = point.X - _dragStartPoint.X;
            double dy = point.Y - _dragStartPoint.Y;

            bool onlyVertical = shift && Math.Abs(dy) >= Math.Abs(dx);
            bool onlyHorizontal = shift && Math.Abs(dx) > Math.Abs(dy);

            bool canUpdateFrequency = !onlyVertical && vm.CanEditProperty(band.Frequency, CurrentTime);
            float effectiveFreq = canUpdateFrequency
                ? FrequencyFromX(point.X)
                : vm.GetEffectiveValue(band.Frequency, CurrentTime);

            if (!onlyHorizontal && IsGainAdjustable(band.FilterType.CurrentValue)
                && vm.CanEditProperty(band.Gain, CurrentTime))
            {
                float targetCompositeDb = GainFromY(point.Y);
                float othersContribution = CalculateResponseDbExcluding(effectiveFreq, _draggingIndex);
                float responseRatio = GainToResponseRatioAtF0(band.FilterType.CurrentValue);
                float newGain = Math.Clamp((targetCompositeDb - othersContribution) / responseRatio, MinGain, MaxGain);
                float oldGain = vm.GetEffectiveValue(band.Gain, CurrentTime);
                if (!AreEqual(oldGain, newGain))
                {
                    vm.SetPropertyValue(band.Gain, CurrentTime, newGain);
                }
            }

            if (canUpdateFrequency)
            {
                float oldFreq = vm.GetEffectiveValue(band.Frequency, CurrentTime);
                if (!AreEqual(oldFreq, effectiveFreq))
                {
                    vm.SetPropertyValue(band.Frequency, CurrentTime, effectiveFreq);
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
        if (_draggingIndex >= 0 && _bandViewModels is not null && _draggingIndex < _bandViewModels.Count)
        {
            var vm = _bandViewModels[_draggingIndex];
            float newGain = vm.GetEffectiveValue(vm.Band.Gain, CurrentTime);
            float newFreq = vm.GetEffectiveValue(vm.Band.Frequency, CurrentTime);

            if (!AreEqual(newGain, _dragStartGain) || !AreEqual(newFreq, _dragStartFrequency))
            {
                vm.Commit();
            }
        }

        _draggingIndex = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_bandViewModels is null) return;

        var point = e.GetPosition(this);
        int hit = HitTestHandle(point);
        if (hit < 0) return;

        var vm = _bandViewModels[hit];
        if (!vm.CanEditProperty(vm.Band.Q, CurrentTime)) return;
        if (e.Delta.Y == 0) return;
        float oldQ = vm.GetEffectiveValue(vm.Band.Q, CurrentTime);
        double factor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
        float newQ = Math.Clamp((float)(oldQ * factor), MinQ, MaxQ);
        if (!AreEqual(oldQ, newQ))
        {
            if (_wheelBandIndex != hit)
            {
                FlushWheelCommit();
                _wheelBandIndex = hit;
                _wheelBandVm = vm;
                _wheelStartQ = oldQ;
            }

            vm.SetPropertyValue(vm.Band.Q, CurrentTime, newQ);
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
        var vm = _wheelBandVm;
        _wheelBandVm = null;
        _wheelBandIndex = -1;
        if (vm is null) return;

        float newQ = vm.GetEffectiveValue(vm.Band.Q, CurrentTime);
        if (!AreEqual(_wheelStartQ, newQ))
        {
            vm.Commit();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (BandViewModels is { } vms)
        {
            vms.CollectionChanged -= OnBandsCollectionChanged;
            vms.CollectionChanged += OnBandsCollectionChanged;
        }
        _bandViewModels = BandViewModels;
        ResubscribeBandProperties();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _wheelCommitTimer?.Stop();
        FlushWheelCommit();

        if (BandViewModels is { } vms)
        {
            vms.CollectionChanged -= OnBandsCollectionChanged;
        }
        foreach (var unsubscribe in _bandSubscriptions)
        {
            unsubscribe();
        }
        _bandSubscriptions.Clear();

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
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

        context.FillRectangle(s_backgroundBrush, new Rect(0, 0, w, h));

        for (int db = -24; db <= 24; db += 6)
        {
            double y = YFromGain(db);
            context.DrawLine(db == 0 ? s_gridStrongPen : s_gridPen, new Point(0, y), new Point(w, y));
        }

        foreach (double freq in new double[] { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            double x = XFromFrequency((float)freq);
            bool isDecade = Math.Abs(Math.Log10(freq) - Math.Round(Math.Log10(freq))) < 0.01;
            context.DrawLine(isDecade ? s_gridStrongPen : s_gridPen, new Point(x, 0), new Point(x, h));
        }

        if (_bandViewModels is { Count: > 0 })
        {
            const int samples = 256;
            var points = new Point[samples];
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / (samples - 1);
                float freq = (float)(MinFrequency * Math.Pow(MaxFrequency / MinFrequency, t));
                float db = CalculateResponseDb(freq);
                double x = XFromFrequency(freq);
                double y = YFromGain(db);
                points[i] = new Point(x, y);
            }

            for (int i = 1; i < samples; i++)
            {
                context.DrawLine(s_curvePen, points[i - 1], points[i]);
            }

            for (int i = 0; i < _bandViewModels.Count; i++)
            {
                var vm = _bandViewModels[i];
                if (!vm.Band.IsEnabled) continue;

                float freq = vm.GetEffectiveValue(vm.Band.Frequency, CurrentTime);
                double x = XFromFrequency(freq);
                double y = YFromGain(CalculateResponseDb(freq));
                bool isSelected = i == SelectedBandIndex;
                bool isHover = i == _hoverIndex || i == _draggingIndex;
                double radius = isSelected || isHover ? HandleRadius + 2 : HandleRadius;

                var fillBrush = isSelected ? s_handleSelectedBrush : s_handleNormalBrush;

                context.DrawEllipse(fillBrush, s_handleStrokePen, new Point(x, y), radius, radius);
            }
        }
    }

    private int HitTestHandle(Point point)
    {
        if (_bandViewModels is null) return -1;
        for (int i = 0; i < _bandViewModels.Count; i++)
        {
            var vm = _bandViewModels[i];
            if (!vm.Band.IsEnabled) continue;

            float freq = vm.GetEffectiveValue(vm.Band.Frequency, CurrentTime);
            double x = XFromFrequency(freq);
            double y = YFromGain(CalculateResponseDb(freq));
            double dx = point.X - x;
            double dy = point.Y - y;
            if (dx * dx + dy * dy <= HitTestRadius * HitTestRadius)
            {
                return i;
            }
        }
        return -1;
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
        // Clamp to session Nyquist so dragging cannot set a target the filter will silently rewrite.
        return (float)Math.Clamp(f, MinFrequency, EffectiveMaxFrequency);
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

    private float CalculateResponseDb(float frequency)
    {
        if (_bandViewModels is null) return 0f;
        double total = 0.0;
        for (int i = 0; i < _bandViewModels.Count; i++)
        {
            var vm = _bandViewModels[i];
            if (!vm.Band.IsEnabled) continue;
            total += CalculateBandResponseDb(frequency, vm);
        }
        return (float)total;
    }

    private float CalculateResponseDbExcluding(float frequency, int excludeIndex)
    {
        if (_bandViewModels is null) return 0f;
        double total = 0.0;
        for (int i = 0; i < _bandViewModels.Count; i++)
        {
            if (i == excludeIndex) continue;
            var vm = _bandViewModels[i];
            if (!vm.Band.IsEnabled) continue;
            total += CalculateBandResponseDb(frequency, vm);
        }
        return (float)total;
    }

    private double CalculateBandResponseDb(float frequency, EqualizerBandItemViewModel vm)
    {
        var band = vm.Band;
        float f0 = vm.GetEffectiveValue(band.Frequency, CurrentTime);
        float q = Math.Max(vm.GetEffectiveValue(band.Q, CurrentTime), MinQ);
        float gain = vm.GetEffectiveValue(band.Gain, CurrentTime);
        return BiQuadFilter.CalculateResponseDb(
            band.FilterType.CurrentValue, f0, q, gain, EffectiveSampleRate, frequency);
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

    private static bool AreEqual(float a, float b) => Math.Abs(a - b) < 1e-5f;
}
