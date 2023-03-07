using Avalonia;

using Beutl.Animation;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class GraphEditorKeyFrameViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly GraphEditorViewModel _parent;
    private readonly ReactivePropertySlim<GraphEditorKeyFrameViewModel?> _previous = new();

    public GraphEditorKeyFrameViewModel(IKeyFrame keyframe, IKeyFrameAnimation animation, GraphEditorViewModel parent)
    {
        Model = keyframe;
        Animation = animation;
        _parent = parent;

        EndY = Model.ObserveProperty(x => x.Value)
            .Select(ToDouble)
            .CombineLatest(parent.ScaleY)
            .Select(x => x.First * x.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        StartY = _previous.Select(x => x?.EndY ?? EndY)
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Height = StartY.CombineLatest(EndY)
            .Select(o => o.Second - o.First)
            .Select(Math.Abs)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Left = _previous.Select(x => x?.Right ?? Observable.Return(0d))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Right = keyframe.GetObservable(KeyFrame.KeyTimeProperty)
            .CombineLatest(parent.Options)
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Width = Right.CombineLatest(Left)
            .Select(x => x.First - x.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Margin = Left.Select(v => new Thickness(v, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Baseline = parent.Baseline;

        BoundsMargin = StartY.CombineLatest(EndY)
            .Select(v => Math.Max(v.First, v.Second))
            .CombineLatest(Baseline)
            .Select(v => new Thickness(0, v.Second - v.First - 16, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        BoundsHeight = Height
            .Select(v => v + (16 * 2))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public IKeyFrame Model { get; }

    public IKeyFrameAnimation Animation { get; }

    public ReadOnlyReactivePropertySlim<double> Left { get; }

    public ReactiveProperty<double> Right { get; }

    public ReadOnlyReactivePropertySlim<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Thickness> Margin { get; }

    public ReadOnlyReactivePropertySlim<double> StartY { get; }

    public ReactiveProperty<double> EndY { get; } = new();

    public ReadOnlyReactivePropertySlim<double> Height { get; }

    public ReactivePropertySlim<double> Baseline { get; }

    public ReadOnlyReactivePropertySlim<Thickness> BoundsMargin { get; }

    public ReadOnlyReactivePropertySlim<double> BoundsHeight { get; }

    public void SetPrevious(GraphEditorKeyFrameViewModel? previous)
    {
        _previous.Value = previous;
    }

    public void Dispose()
    {
        _previous.Value = null;
        _disposables.Dispose();
    }

    public static double ToDouble(object? value)
    {
        try
        {
            return Convert.ToDouble(value);
        }
        catch
        {
            return 1d;
        }
    }
}
