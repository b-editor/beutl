using System.Collections;
using System.Collections.Specialized;

using Avalonia;
using Avalonia.Threading;

using Beutl.Animation;
using Beutl.ProjectSystem;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class GraphEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly EditViewModel _editViewModel;
    private readonly GraphEditorViewViewModelFactory[] _factories;
    private bool _editting;

    public GraphEditorViewModel(EditViewModel editViewModel, IKeyFrameAnimation animation)
    {
        _editViewModel = editViewModel;
        Animation = animation;

        PanelWidth = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(editViewModel.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SeekBarMargin = Scene.GetObservable(Scene.CurrentFrameProperty)
            .CombineLatest(editViewModel.Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        EndingBarMargin = PanelWidth.Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        _factories = GraphEditorViewViewModelFactory.GetFactory(this).ToArray();
        Factory = _factories.FirstOrDefault();
        Views = GraphEditorViewViewModelFactory.CreateViews(this, Factory);
        foreach (GraphEditorViewViewModel item in Views)
        {
            item.VerticalRangeChanged += OnItemVerticalRangeChanged;
        }

        SelectedView.Value = Views.FirstOrDefault();

        CalculateMaxHeight();

        editViewModel.Offset.Subscribe(v => ScrollOffset.Value = ScrollOffset.Value.WithX(v.X))
            .DisposeWith(_disposables);
    }

    public IReactiveProperty<TimelineOptions> Options => _editViewModel.Options;

    public Scene Scene => _editViewModel.Scene;

    public ReactivePropertySlim<double> ScaleY { get; } = new(0.5);

    public ReactivePropertySlim<Vector> ScrollOffset { get; } = new();

    public double Padding { get; } = 120;

    public ReactivePropertySlim<double> MinHeight { get; } = new();

    // Zeroの位置
    public ReactivePropertySlim<double> Baseline { get; } = new();

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReactivePropertySlim<GraphEditorViewViewModel?> SelectedView { get; } = new();

    public IKeyFrameAnimation Animation { get; }

    public GraphEditorViewViewModel[] Views { get; }

    public GraphEditorViewViewModelFactory? Factory { get; }

    public void BeginEditing()
    {
        _editting = true;
    }

    public void EndEditting()
    {
        _editting = false;
        CalculateMaxHeight();
    }

    private void OnItemVerticalRangeChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(CalculateMaxHeight);
    }

    private void CalculateMaxHeight()
    {
        double max = 0d;
        double min = 0d;
        foreach (GraphEditorViewViewModel view in Views)
        {
            view.GetVerticalRange(ref min, ref max);
        }

        double oldbase = Baseline.Value;
        double newBase = max + Padding;
        double delta = newBase - oldbase;

        double oldHeight = MinHeight.Value;
        double newHeight = max - min + (Padding * 2);

        if (!_editting || newHeight > oldHeight)
        {
            MinHeight.Value = newHeight;
            Baseline.Value = newBase;

            ScrollOffset.Value = new Vector(ScrollOffset.Value.X, Math.Max(0, ScrollOffset.Value.Y + delta));
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        foreach (GraphEditorViewViewModel item in Views)
        {
            item.VerticalRangeChanged -= OnItemVerticalRangeChanged;
            item.Dispose();
        }
    }
}
