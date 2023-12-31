using Avalonia;
using Avalonia.Media;

using Beutl.Animation;
using Beutl.Commands;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using SplineEasing = Beutl.Animation.Easings.SplineEasing;

namespace Beutl.ViewModels;

public sealed class GraphEditorKeyFrameViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly GraphEditorViewViewModel _parent;
    internal readonly ReactivePropertySlim<GraphEditorKeyFrameViewModel?> _previous = new();
    internal GraphEditorKeyFrameViewModel? _next;

    public GraphEditorKeyFrameViewModel(
        IKeyFrame keyframe,
        GraphEditorViewViewModel parent)
    {
        Model = keyframe;
        _parent = parent;

        EndY = Model.ObserveProperty(x => x.Value)
            .Select(_parent.ConvertToDouble)
            .CombineLatest(parent.Parent.ScaleY)
            .Select(x => x.First * x.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        StartY = _previous.Select(x => x?.EndY ?? EndY)
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Decreasing = StartY.CombineLatest(EndY)
            .Select(x => x.First > x.Second)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Height = StartY.CombineLatest(EndY)
            .Select(o => o.Second - o.First)
            .Select(Math.Abs)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Left = _previous
            .Select(x => x?.Right ?? _parent.Parent.Margin.Select(x => -x.Left))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Right = keyframe.GetObservable(KeyFrame.KeyTimeProperty)
            .CombineLatest(parent.Parent.Options)
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

        Baseline = parent.Parent.Baseline;

        BoundsMargin = StartY.CombineLatest(EndY)
            .Select(v => Math.Max(v.First, v.Second))
            .CombineLatest(Baseline)
            .Select(v => new Thickness(0, v.Second - v.First, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsSplineEasing = keyframe.GetObservable(KeyFrame.EasingProperty)
            .Select(v => v is SplineEasing)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IObservable<(Vector, Vector)> controlPointObservable = keyframe.GetObservable(KeyFrame.EasingProperty)
            .Select(v =>
            {
                if (v is SplineEasing splineEasing)
                {
                    (Vector, Vector) ToVector()
                    {
                        return (new Vector(splineEasing.X1, splineEasing.Y1), new Vector(splineEasing.X2, splineEasing.Y2));
                    }

                    return Observable.FromEventPattern(splineEasing, nameof(SplineEasing.Changed))
                        .Select(_ => ToVector())
                        .Publish(ToVector())
                        .RefCount();
                }
                else
                {
                    return Observable.Return<(Vector, Vector)>(default);
                }
            })
            .Switch();

        ControlPoint1 = controlPointObservable
            .Select(v => v.Item1)
            .CombineLatest(Decreasing)
            .Select(v => v.First.WithY(v.Second ? v.First.Y : 1 - v.First.Y))
            .CombineLatest(Width, Height, (pt, w, h) => (Point)Vector.Multiply(pt, new Vector(w, h)))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ControlPoint2 = controlPointObservable
            .Select(v => v.Item2)
            .CombineLatest(Decreasing)
            .Select(v => v.First.WithY(v.Second ? v.First.Y : 1 - v.First.Y))
            .CombineLatest(Width, Height, (pt, w, h) => (Point)Vector.Multiply(pt, new Vector(w, h)))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        LeftBottom = Height
            .CombineLatest(Decreasing)
            .Select(v => v.Second ? default : new Point(0, v.First))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        RightTop = Width
            .CombineLatest(Height, Decreasing)
            .Select(v => v.Third ? new Point(v.First, v.Second) : new Point(v.First, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public IKeyFrame Model { get; }

    public ReadOnlyReactivePropertySlim<IBrush?> Stroke => _parent.Stroke;

    public ReadOnlyReactivePropertySlim<bool> Decreasing { get; }

    public ReadOnlyReactivePropertySlim<double> Left { get; }

    public ReactiveProperty<double> Right { get; }

    public ReadOnlyReactivePropertySlim<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Thickness> Margin { get; }

    public ReadOnlyReactivePropertySlim<double> StartY { get; }

    public ReactiveProperty<double> EndY { get; } = new();

    public ReadOnlyReactivePropertySlim<double> Height { get; }

    public ReactivePropertySlim<double> Baseline { get; }

    public ReadOnlyReactivePropertySlim<Thickness> BoundsMargin { get; }

    public ReadOnlyReactivePropertySlim<bool> IsSplineEasing { get; }

    public ReadOnlyReactivePropertySlim<Point> ControlPoint1 { get; }

    public ReadOnlyReactivePropertySlim<Point> ControlPoint2 { get; }

    public ReadOnlyReactivePropertySlim<Point> LeftBottom { get; }

    public ReadOnlyReactivePropertySlim<Point> RightTop { get; }

    public void SetPrevious(GraphEditorKeyFrameViewModel? previous)
    {
        _previous.Value = previous;
        if (previous != null)
        {
            previous._next = this;
        }
    }

    public void Dispose()
    {
        _previous.Value = null;
        _disposables.Dispose();
    }

    private (double X, double Y) CoerceControlPoint(Point point)
    {
        double x = point.X / Width.Value;
        x = Math.Clamp(x, 0, 1);
        double y;

        if (!Decreasing.Value)
        {
            y = -(point.Y / Height.Value) + 1;
        }
        else
        {
            y = point.Y / Height.Value;
        }

        return (x, y);
    }

    public void UpdateControlPoint1(Point point)
    {
        if (Model.Easing is SplineEasing splineEasing)
        {
            (double x, double y) = CoerceControlPoint(point);

            if (double.IsFinite(x))
            {
                splineEasing.X1 = (float)x;
            }
            if (double.IsFinite(y))
            {
                splineEasing.Y1 = (float)y;
            }
        }
    }

    public void UpdateControlPoint2(Point point)
    {
        if (Model.Easing is SplineEasing splineEasing)
        {
            (double x, double y) = CoerceControlPoint(point);

            if (double.IsFinite(x))
            {
                splineEasing.X2 = (float)x;
            }
            if (double.IsFinite(y))
            {
                splineEasing.Y2 = (float)y;
            }
        }
    }

    public void SubmitControlPoint1(float oldX, float oldY)
    {
        if (Model.Easing is SplineEasing splineEasing)
        {
            var oldValues = (oldX, oldY);
            var newValues = (splineEasing.X1, splineEasing.Y1);
            if (oldValues == newValues)
                return;
            var command = new SubmitControlPointCommand(oldValues, newValues, splineEasing, true);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }

    public void SubmitControlPoint2(float oldX, float oldY)
    {
        if (Model.Easing is SplineEasing splineEasing)
        {
            var oldValues = (oldX, oldY);
            var newValues = (splineEasing.X2, splineEasing.Y2);
            if (oldValues == newValues)
                return;
            var command = new SubmitControlPointCommand(oldValues, newValues, splineEasing, false);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }

    public void SubmitCrossed(TimeSpan timeSpan)
    {
        int rate = _parent.Parent.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        Model.KeyTime = timeSpan.RoundToRate(rate);
    }

    public void SubmitKeyTimeAndValue(TimeSpan oldKeyTime)
    {
        GraphEditorViewModel parent2 = _parent.Parent;
        IKeyFrameAnimation animation = parent2.Animation;

        float scale = parent2.Options.Value.Scale;
        int rate = parent2.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

        if (_parent.TryConvertFromDouble(Model.Value, EndY.Value / parent2.ScaleY.Value, animation.Property.PropertyType, out object? obj))
        {
            var command = new SubmitKeyFrameCommand(
                keyframe: Model,
                oldTime: oldKeyTime,
                newTime: Right.Value.ToTimeSpan(scale).RoundToRate(rate),
                oldValue: Model.Value,
                newValue: obj);
            command.DoAndRecord(CommandRecorder.Default);
            EndY.Value = _parent.ConvertToDouble(Model.Value) * parent2.ScaleY.Value;
        }
        else
        {
            var command = new ChangePropertyCommand<TimeSpan>(
                obj: Model,
                property: KeyFrame.KeyTimeProperty,
                newValue: Right.Value.ToTimeSpan(scale).RoundToRate(rate),
                oldValue: oldKeyTime);
            command.DoAndRecord(CommandRecorder.Default);
        }

        Right.Value = Model.KeyTime.ToPixel(_parent.Parent.Options.Value.Scale);
    }

    private sealed class SubmitControlPointCommand(
        (float, float) oldValue, (float, float) newValue, SplineEasing splineEasing, bool first)
        : IRecordableCommand
    {
        public void Do()
        {
            if (first)
            {
                splineEasing.X1 = newValue.Item1;
                splineEasing.Y1 = newValue.Item2;
            }
            else
            {
                splineEasing.X2 = newValue.Item1;
                splineEasing.Y2 = newValue.Item2;
            }
        }

        public void Redo() => Do();

        public void Undo()
        {
            if (first)
            {
                splineEasing.X1 = oldValue.Item1;
                splineEasing.Y1 = oldValue.Item2;
            }
            else
            {
                splineEasing.X2 = oldValue.Item1;
                splineEasing.Y2 = oldValue.Item2;
            }
        }
    }

    private sealed class SubmitKeyFrameCommand(IKeyFrame keyframe, TimeSpan oldTime, TimeSpan newTime, object? oldValue, object? newValue)
        : IRecordableCommand
    {
        public void Do()
        {
            keyframe.Value = newValue;
            keyframe.KeyTime = newTime;
        }

        public void Redo() => Do();

        public void Undo()
        {
            keyframe.Value = oldValue;
            keyframe.KeyTime = oldTime;
        }
    }
}
