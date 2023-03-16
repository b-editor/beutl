using System.Collections;
using System.Collections.Specialized;

using Avalonia;

using Beutl.Animation;
using Beutl.ProjectSystem;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class GraphEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly EditViewModel _editViewModel;
    private bool _editting;

    public GraphEditorViewModel(EditViewModel editViewModel, IKeyFrameAnimation animation)
    {
        _editViewModel = editViewModel;
        Animation = animation;
        Animation.KeyFrames.CollectionChanged += OnKeyFramesCollectionChanged;

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

        AddKeyFrames();
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

    public IKeyFrameAnimation Animation { get; }

    public CoreList<GraphEditorKeyFrameViewModel> KeyFrames { get; } = new();

    public void BeginEditing()
    {
        _editting = true;
    }

    public void EndEditting()
    {
        _editting = false;
        CalculateMaxHeight();
    }

    private void CalculateMaxHeight()
    {
        double max = 0d;
        double min = 0d;
        foreach (GraphEditorKeyFrameViewModel item in KeyFrames)
        {
            max = Math.Max(max, item.EndY.Value);
            min = Math.Min(min, item.EndY.Value);

            if (item.IsSplineEasing.Value)
            {
                double c1 = item.ControlPoint1.Value.Y;
                double c2 = item.ControlPoint2.Value.Y;
                c1 = -c1 + Math.Max(item.EndY.Value, item.StartY.Value);
                c2 = -c2 + Math.Max(item.EndY.Value, item.StartY.Value);

                max = Math.Max(max, c1);
                min = Math.Min(min, c1);
                max = Math.Max(max, c2);
                min = Math.Min(min, c2);
            }
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

    private void AddKeyFrames()
    {
        GraphEditorKeyFrameViewModel? prev = null;
        int index = 0;
        foreach (IKeyFrame item in Animation.KeyFrames)
        {
            var viewModel = new GraphEditorKeyFrameViewModel(item, Animation, this);
            viewModel.EndY.Subscribe(_ => CalculateMaxHeight());
            viewModel.ControlPoint1.Subscribe(_ => CalculateMaxHeight());
            viewModel.ControlPoint2.Subscribe(_ => CalculateMaxHeight());
            viewModel.SetPrevious(prev);
            KeyFrames.Insert(index++, viewModel);
            prev = viewModel;
        }
    }

    private void OnKeyFramesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        GraphEditorKeyFrameViewModel? TryGet(int index)
        {
            if (0 <= index && index < KeyFrames.Count)
            {
                return KeyFrames[index];
            }
            else
            {
                return null;
            }
        }

        // | NewItem 1 | NewItem 2 | NewItem 3 | Existing | ...
        //          ^     /     ^     /     ^     /
        //           \---/       \---/       \---/
        void Add(int index, IList items)
        {
            foreach (IKeyFrame item in items)
            {
                var viewModel = new GraphEditorKeyFrameViewModel(item, Animation, this);
                viewModel.EndY.Subscribe(_ => CalculateMaxHeight());
                viewModel.ControlPoint1.Subscribe(_ => CalculateMaxHeight());
                viewModel.ControlPoint2.Subscribe(_ => CalculateMaxHeight());
                viewModel.SetPrevious(TryGet(index - 1));
                KeyFrames.Insert(index, viewModel);
                index++;
            }

            GraphEditorKeyFrameViewModel? existing = TryGet(index);
            existing?.SetPrevious(TryGet(index - 1));
        }

        // |  Existing | OldItem 1 | OldItem 2 | Existing | ...
        //          ^                             /
        //           \---------------------------/
        void Remove(int index, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                KeyFrames[index + i].Dispose();
            }

            KeyFrames.RemoveRange(index, count);

            GraphEditorKeyFrameViewModel? existing = TryGet(index);
            existing?.SetPrevious(TryGet(index - 1));
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                Remove(e.OldStartingIndex, e.OldItems!.Count);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                Remove(e.OldStartingIndex, e.OldItems!.Count);
                break;

            case NotifyCollectionChangedAction.Reset:
                Remove(0, KeyFrames.Count);
                break;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        Animation.KeyFrames.CollectionChanged -= OnKeyFramesCollectionChanged;
        foreach (GraphEditorKeyFrameViewModel item in KeyFrames)
        {
            item.Dispose();
        }
    }
}
