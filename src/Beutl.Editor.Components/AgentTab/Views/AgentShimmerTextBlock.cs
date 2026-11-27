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

    private readonly List<IDisposable> _visibilitySubscriptions = [];
    private DispatcherTimer? _timer;
    private LinearGradientBrush? _gradient;
    private long _started;

    public bool IsLive { get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value); }
    public IBrush MutedBrush { get => GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public IBrush HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var ancestor in this.GetVisualAncestors())
            _visibilitySubscriptions.Add(ancestor.GetObservable(IsVisibleProperty).Subscribe(_ => RefreshAnimation()));
        RefreshAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var subscription in _visibilitySubscriptions)
            subscription.Dispose();
        _visibilitySubscriptions.Clear();
        StopAnimation();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsLiveProperty || change.Property == IsVisibleProperty
            || change.Property == MutedBrushProperty || change.Property == HighlightBrushProperty)
            RefreshAnimation();
    }

    private void RefreshAnimation()
    {
        if (!IsLive || !this.IsAttachedToVisualTree() || !IsVisible || this.GetVisualAncestors().Any(ancestor => !ancestor.IsVisible))
        {
            StopAnimation();
            return;
        }

        var muted = (MutedBrush as ISolidColorBrush)?.Color ?? Colors.Gray;
        var highlight = (HighlightBrush as ISolidColorBrush)?.Color ?? Colors.White;
        _gradient ??= new LinearGradientBrush();
        _gradient.GradientStops =
        [
            new GradientStop(muted, 0), new GradientStop(muted, .18), new GradientStop(highlight, .5),
            new GradientStop(muted, .82), new GradientStop(muted, 1)
        ];
        Foreground = _gradient;
        if (_timer is null)
        {
            _started = Stopwatch.GetTimestamp();
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(32) };
            _timer.Tick += OnTick;
            _timer.Start();
        }
        UpdateGradient();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (IsEffectivelyVisible)
            UpdateGradient();
        else
            StopAnimation();
    }

    private void UpdateGradient()
    {
        if (_gradient is not { } gradient)
            return;
        double phase = Stopwatch.GetElapsedTime(_started).TotalSeconds % 1.6 / 1.6;
        double left = -.48 + phase * 1.48;
        gradient.StartPoint = new RelativePoint(left, .5, RelativeUnit.Relative);
        gradient.EndPoint = new RelativePoint(left + .48, .5, RelativeUnit.Relative);
    }

    private void StopAnimation()
    {
        if (_timer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnTick;
            _timer = null;
        }
        _gradient = null;
        Foreground = MutedBrush;
    }
}
