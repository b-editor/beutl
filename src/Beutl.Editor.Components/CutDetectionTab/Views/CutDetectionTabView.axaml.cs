using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Beutl.Editor.Components.CutDetectionTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Media;

namespace Beutl.Editor.Components.CutDetectionTab.Views;

public partial class CutDetectionTabView : UserControl
{
    private IDisposable? _subscription;
    private IDisposable? _clockSubscription;
    private CutDetectionViewModel? _analysis;
    private bool _attached;

    public CutDetectionTabView()
    {
        InitializeComponent();
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
        _clockSubscription?.Dispose();
        _clockSubscription = null;
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
        _clockSubscription?.Dispose();
        SetAnalysis(null);
        var tab = DataContext as CutDetectionTabViewModel;
        _subscription = tab?.Analysis.Subscribe(SetAnalysis);
        _clockSubscription = tab?.CurrentTime?.Subscribe(_ => UpdatePlayhead());
    }

    private void SetAnalysis(CutDetectionViewModel? analysis)
    {
        if (_analysis != null) _analysis.PreviewChanged -= OnPreviewChanged;
        _analysis = analysis;
        if (_analysis != null) _analysis.PreviewChanged += OnPreviewChanged;
        OnPreviewChanged(this, EventArgs.Empty);
    }

    private void OnPreviewChanged(object? sender, EventArgs e)
    {
        Preview.SetData(_analysis?.Frames ?? [], _analysis?.ClipRange ?? default,
            _analysis?.Candidates ?? [], (double)(_analysis?.Threshold.Value ?? 0.3m));
        double duration = _analysis?.ClipRange.Duration.ToSeconds() ?? 0;
        PreviewStartTime.Text = FormatTime(0);
        PreviewEndTime.Text = FormatTime(duration);
        UpdatePlayhead();
    }

    private void UpdatePlayhead()
    {
        MediaTime? time = (DataContext as CutDetectionTabViewModel)?.CurrentTime?.Value;
        Preview.SetPlayhead(_analysis != null && time is { } current && _analysis.ClipRange.Contains(current)
            ? current
            : null);
    }

    private static string FormatTime(double seconds)
    {
        long centiseconds = (long)Math.Round(seconds * 100);
        return $"{centiseconds / 360000:00}:{centiseconds / 6000 % 60:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}";
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        bool wide = e.NewSize.Width >= 800;
        ContentLayout.ColumnDefinitions = new(wide ? "*,360" : "*");
        ContentLayout.RowDefinitions = new(wide ? "*" : "Auto,Auto");
        Grid.SetColumn(OptionsScroll, wide ? 1 : 0);
        Grid.SetRow(OptionsScroll, wide ? 0 : 1);
        BodyScroll.VerticalScrollBarVisibility = wide ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        OptionsScroll.VerticalScrollBarVisibility = wide ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Preview.Height = wide ? double.NaN : 140;
        Preview.MinHeight = wide ? 0 : 90;
        PreviewPane.RowSpacing = wide ? 4 : 8;
        ParameterGroup.Padding = new Thickness(wide ? 8 : 12);
        OptionsStack.Spacing = wide ? 8 : 12;
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => (DataContext as CutDetectionTabViewModel)?.Refresh();

    private async void OnSplitClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CutDetectionTabViewModel model) await model.ApplySplitAsync();
    }

    private async void OnAddMarkersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CutDetectionTabViewModel model) await model.ApplyMarkersAsync();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _analysis?.CancelAnalysis();
}
