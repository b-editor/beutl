using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public abstract class AudioVisualizerControlBase : Control
{
    public static readonly StyledProperty<AudioSampleRingBuffer?> RingBufferProperty =
        AvaloniaProperty.Register<AudioVisualizerControlBase, AudioSampleRingBuffer?>(nameof(RingBuffer));

    public static readonly StyledProperty<MediaTime> PlayheadTimeProperty =
        AvaloniaProperty.Register<AudioVisualizerControlBase, MediaTime>(nameof(PlayheadTime));

    public static readonly StyledProperty<IBrush> SecondaryBrushProperty =
        AvaloniaProperty.Register<AudioVisualizerControlBase, IBrush>(
            nameof(SecondaryBrush), new SolidColorBrush(Color.FromArgb(160, 120, 200, 255)));

    public static readonly StyledProperty<IBrush> PrimaryBrushProperty =
        AvaloniaProperty.Register<AudioVisualizerControlBase, IBrush>(
            nameof(PrimaryBrush), new SolidColorBrush(Color.FromArgb(220, 90, 255, 160)));

    private DispatcherTimer? _timer;

    public AudioSampleRingBuffer? RingBuffer
    {
        get => GetValue(RingBufferProperty);
        set => SetValue(RingBufferProperty, value);
    }

    public MediaTime PlayheadTime
    {
        get => GetValue(PlayheadTimeProperty);
        set => SetValue(PlayheadTimeProperty, value);
    }

    public IBrush PrimaryBrush
    {
        get => GetValue(PrimaryBrushProperty);
        set => SetValue(PrimaryBrushProperty, value);
    }

    public IBrush SecondaryBrush
    {
        get => GetValue(SecondaryBrushProperty);
        set => SetValue(SecondaryBrushProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, Dispatcher, (_, _) => InvalidateVisual());
        UpdateTimerState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            UpdateTimerState();
        }
    }

    private void UpdateTimerState()
    {
        if (_timer == null) return;
        if (IsVisible) _timer.Start();
        else _timer.Stop();
    }
}
