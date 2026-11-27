using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed class AgentShimmerTextBlock : TextBlock
{
    public static readonly StyledProperty<bool> IsLiveProperty =
        AvaloniaProperty.Register<AgentShimmerTextBlock, bool>(nameof(IsLive));
    public static readonly StyledProperty<IBrush> MutedBrushProperty =
        AvaloniaProperty.Register<AgentShimmerTextBlock, IBrush>(nameof(MutedBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> HighlightBrushProperty =
        AvaloniaProperty.Register<AgentShimmerTextBlock, IBrush>(nameof(HighlightBrush), Brushes.White);

    private static readonly HashSet<AgentShimmerTextBlock> s_active = [];
    private static DispatcherTimer? s_timer;

    private readonly List<IDisposable> _visibilitySubscriptions = [];
    private LinearGradientBrush? _gradient;
    private long _started;
    private bool _running;
    private bool _subscribingVisibility;

    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }
    public IBrush MutedBrush { get => GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public IBrush HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearVisibilitySubscriptions();
        StopAnimation();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsLiveProperty || change.Property == IsVisibleProperty
            || change.Property == MutedBrushProperty || change.Property == HighlightBrushProperty)
        {
            RefreshAnimation();
        }
        else if (_running && change.Property == BoundsProperty)
        {
            UpdateGradient(Stopwatch.GetTimestamp());
        }
    }

    private void EnsureVisibilitySubscriptions()
    {
        if (_subscribingVisibility || _visibilitySubscriptions.Count > 0 || !this.IsAttachedToVisualTree())
            return;
        _subscribingVisibility = true;
        try
        {
            foreach (var ancestor in this.GetVisualAncestors())
            {
                _visibilitySubscriptions.Add(ancestor.GetObservable(IsVisibleProperty).Subscribe(_ =>
                {
                    if (!_subscribingVisibility)
                        RefreshAnimation();
                }));
            }
        }
        finally
        {
            _subscribingVisibility = false;
        }
    }

    private void ClearVisibilitySubscriptions()
    {
        if (_visibilitySubscriptions.Count == 0)
            return;
        foreach (var subscription in _visibilitySubscriptions)
            subscription.Dispose();
        _visibilitySubscriptions.Clear();
    }

    private void RefreshAnimation()
    {
        if (!IsLive || !this.IsAttachedToVisualTree())
        {
            ClearVisibilitySubscriptions();
            StopAnimation();
            return;
        }

        EnsureVisibilitySubscriptions();
        if (!IsVisible || this.GetVisualAncestors().Any(ancestor => !ancestor.IsVisible))
        {
            StopAnimation();
            return;
        }

        var muted = (MutedBrush as ISolidColorBrush)?.Color ?? Colors.Gray;
        var highlight = (HighlightBrush as ISolidColorBrush)?.Color ?? Colors.White;
        _gradient ??= new LinearGradientBrush();
        _gradient.GradientStops =
        [
            new GradientStop(muted, 0),
            new GradientStop(muted, .18),
            new GradientStop(highlight, .5),
            new GradientStop(muted, .82),
            new GradientStop(muted, 1)
        ];
        Foreground = _gradient;
        if (!_running)
        {
            _running = true;
            _started = Stopwatch.GetTimestamp();
            Register(this);
        }
        UpdateGradient(Stopwatch.GetTimestamp());
    }

    private void UpdateGradient(long now)
    {
        if (_gradient is not { } gradient)
            return;
        // Use control-local pixel coordinates (RelativeUnit.Absolute) so mixed Latin/CJK font runs
        // share one continuous sweep instead of restarting the gradient per shaped run.
        double width = Math.Max(Bounds.Width, 64);
        double band = Math.Clamp(width * 0.48, 48, 160);
        double phase = Stopwatch.GetElapsedTime(_started, now).TotalSeconds % 1.6 / 1.6;
        double start = -band + phase * (width + band);
        gradient.StartPoint = new RelativePoint(start, 0, RelativeUnit.Absolute);
        gradient.EndPoint = new RelativePoint(start + band, 0, RelativeUnit.Absolute);
    }

    private void StopAnimation()
    {
        if (_running)
        {
            _running = false;
            Unregister(this);
        }
        _gradient = null;
        Foreground = MutedBrush;
    }

    private static void Register(AgentShimmerTextBlock block)
    {
        s_active.Add(block);
        if (s_timer is null)
        {
            s_timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            s_timer.Tick += OnSharedTick;
            s_timer.Start();
        }
    }

    private static void Unregister(AgentShimmerTextBlock block)
    {
        s_active.Remove(block);
        if (s_active.Count == 0 && s_timer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnSharedTick;
            s_timer = null;
        }
    }

    private static void OnSharedTick(object? sender, EventArgs e)
    {
        if (s_active.Count == 0)
            return;
        long now = Stopwatch.GetTimestamp();
        foreach (var block in s_active.ToArray())
        {
            if (block.IsEffectivelyVisible)
                block.UpdateGradient(now);
            else
                block.StopAnimation();
        }
    }
}
