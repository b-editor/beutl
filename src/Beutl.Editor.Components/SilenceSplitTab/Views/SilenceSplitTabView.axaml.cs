using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.SilenceSplitTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.SilenceSplitTab.Views;

public partial class SilenceSplitTabView : UserControl
{
    private IDisposable? _subscription;
    private SilenceSplitViewModel? _analysis;
    private bool _attached;
    private bool _wide;
    private bool _updatingPreview;

    public SilenceSplitTabView() => InitializeComponent();

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        bool wide = e.NewSize.Width >= 800;
        if (_wide == wide) return;
        _wide = wide;
        ContentLayout.ColumnDefinitions = new(wide ? "*,360" : "*");
        ContentLayout.RowDefinitions = new(wide ? "*" : "Auto,Auto");
        Grid.SetColumn(OptionsScroll, wide ? 1 : 0);
        Grid.SetRow(OptionsScroll, wide ? 0 : 1);
        BodyScroll.VerticalScrollBarVisibility = wide ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        OptionsScroll.VerticalScrollBarVisibility = wide ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Waveform.Height = wide ? double.NaN : 112;
        Waveform.MinHeight = wide ? 0 : 64;
        PreviewPane.RowSpacing = wide ? 4 : 8;
        ParameterGroup.Padding = new(wide ? 8 : 12);
        OptionsStack.Spacing = wide ? 8 : 12;
        ModeStack.Spacing = wide ? 4 : 8;
        FooterLayout.ColumnDefinitions = new(wide ? "*,Auto" : "*");
        FooterLayout.RowDefinitions = new(wide ? "Auto" : "Auto,Auto");
        Grid.SetColumn(ApplyButton, wide ? 1 : 0);
        Grid.SetRow(ApplyButton, wide ? 0 : 1);
        ApplyButton.MinWidth = wide ? 128 : 0;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        ObserveAnalysis();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _subscription?.Dispose();
        _subscription = null;
        SetAnalysis(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) ObserveAnalysis();
    }

    private void ObserveAnalysis()
    {
        _subscription?.Dispose();
        SetAnalysis(null);
        _subscription = (DataContext as SilenceSplitTabViewModel)?.Analysis.Subscribe(SetAnalysis);
    }

    private void SetAnalysis(SilenceSplitViewModel? analysis)
    {
        if (_analysis != null) _analysis.PreviewChanged -= OnPreviewChanged;
        _analysis = analysis;
        if (_analysis != null) _analysis.PreviewChanged += OnPreviewChanged;
        OnPreviewChanged(this, EventArgs.Empty);
    }

    private void OnPreviewChanged(object? sender, EventArgs e)
    {
        Waveform.SetChunks(_analysis?.PreviewChunks ?? []);
        var window = _analysis?.PreviewRange ?? default;
        double duration = window.Duration.ToSeconds();
        var start = (_analysis?.ClipRange.Start ?? MediaTime.Zero) + window.Start;
        var end = start + window.Duration;
        Waveform.SetHighlights(_analysis?.Ranges.Where(r => r.End > start && r.Start < end).Select(r => (
            (r.Start - start).ToSeconds() / duration,
            (r.End - start).ToSeconds() / duration)).ToArray() ?? []);
        PreviewStartTime.Text = FormatTime(window.Start.ToSeconds());
        PreviewEndTime.Text = FormatTime(window.End.ToSeconds());
        ZoomInButton.IsEnabled = _analysis?.CanZoomIn == true;
        ZoomOutButton.IsEnabled = ResetZoomButton.IsEnabled = PreviewScroll.IsEnabled = _analysis?.CanZoomOut == true;
        double total = _analysis?.ClipRange.Duration.ToSeconds() ?? 0;
        ZoomLevel.Text = $"{(duration > 0 ? total / duration : 1):0.#}×";
        _updatingPreview = true;
        try
        {
            PreviewScroll.Maximum = Math.Max(0, total - duration);
            PreviewScroll.ViewportSize = duration;
            PreviewScroll.SmallChange = Math.Max(0.02, duration / 10);
            PreviewScroll.LargeChange = duration;
            PreviewScroll.Value = window.Start.ToSeconds();
        }
        finally { _updatingPreview = false; }
    }

    private static string FormatTime(double seconds)
    {
        long centiseconds = (long)Math.Round(seconds * 100);
        return $"{centiseconds / 360000:00}:{centiseconds / 6000 % 60:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}";
    }

    private void OnPreviewScrollChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingPreview) _analysis?.PanPreview(e.NewValue);
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => _analysis?.ZoomPreview(true);
    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => _analysis?.ZoomPreview(false);
    private void OnResetZoomClick(object? sender, RoutedEventArgs e) => _analysis?.ResetPreview();

    private void OnWaveformWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0 || e.Delta.Y == 0) return;
        _analysis?.ZoomPreview(e.Delta.Y > 0);
        e.Handled = true;
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => (DataContext as SilenceSplitTabViewModel)?.Refresh();

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SilenceSplitTabViewModel model) await model.ApplyAsync();
    }
}
