using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.SilenceSplitTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.SilenceSplitTab.Views;

public partial class SilenceSplitTabView : UserControl
{
    private IDisposable? _subscription;
    private SilenceSplitViewModel? _analysis;
    private bool _attached;

    public SilenceSplitTabView() => InitializeComponent();

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
        Waveform.SetHighlights(_analysis?.Ranges.Select(r => (
            (r.Start - _analysis.ClipRange.Start).ToSeconds() / _analysis.ClipRange.Duration.ToSeconds(),
            (r.End - _analysis.ClipRange.Start).ToSeconds() / _analysis.ClipRange.Duration.ToSeconds())).ToArray() ?? []);
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => (DataContext as SilenceSplitTabViewModel)?.Refresh();

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SilenceSplitTabViewModel model) await model.ApplyAsync();
    }
}
