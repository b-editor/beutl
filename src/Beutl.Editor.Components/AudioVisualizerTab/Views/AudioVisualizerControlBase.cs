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
    private long? _renderedVersion;
    private MediaTime _renderedPlayhead;

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

    // Whether the last render left something changing on its own, such as a falling peak hold.
    protected internal virtual bool IsSettling => false;

    // Analysis is the expensive part of a render, and with the same input and nothing settling it gives the same picture.
    internal bool RequestRenderIfChanged()
    {
        long? version = RingBuffer?.Version;
        if (version == _renderedVersion && PlayheadTime == _renderedPlayhead && !IsSettling) return false;

        _renderedVersion = version;
        _renderedPlayhead = PlayheadTime;
        InvalidateVisual();
        return true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, Dispatcher, (_, _) => RequestRenderIfChanged());
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
        // The view's settings, such as its FFT size. The timer covers the playhead, at its own rate.
        else if (change.Property != PlayheadTimeProperty
                 && typeof(AudioVisualizerControlBase).IsAssignableFrom(change.Property.OwnerType))
        {
            InvalidateVisual();
        }
    }

    private void UpdateTimerState()
    {
        if (_timer == null) return;
        if (IsVisible) _timer.Start();
        else _timer.Stop();
    }
}
