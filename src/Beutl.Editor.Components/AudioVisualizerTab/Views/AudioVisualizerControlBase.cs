using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.Editor.Components.AudioVisualizerTab.Views;

public abstract partial class AudioVisualizerControlBase : Control
{
    private DispatcherTimer? _timer;
    private long? _renderedVersion;
    private MediaTime _renderedPlayhead;

    public static readonly StyledProperty<IBrush> PrimaryBrushProperty =
        AvaloniaProperty.Register<AudioVisualizerControlBase, IBrush>(
            nameof(PrimaryBrush), new SolidColorBrush(Color.FromArgb(220, 90, 255, 160)));

    public static readonly StyledProperty<IBrush> SecondaryBrushProperty =
        AvaloniaProperty.Register<AudioVisualizerControlBase, IBrush>(
            nameof(SecondaryBrush), new SolidColorBrush(Color.FromArgb(160, 120, 200, 255)));

    static AudioVisualizerControlBase()
    {
        // The timer covers the playhead, at its own rate.
        AffectsRender<AudioVisualizerControlBase>(RingBufferProperty, PrimaryBrushProperty, SecondaryBrushProperty);
    }

    [GeneratedStyledProperty]
    public partial AudioSampleRingBuffer? RingBuffer { get; set; }

    [GeneratedStyledProperty]
    public partial MediaTime PlayheadTime { get; set; }

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
    internal bool IsSettling { get; private protected set; }

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
    }

    private void UpdateTimerState()
    {
        if (_timer == null) return;
        if (IsVisible) _timer.Start();
        else _timer.Stop();
    }
}
