using Avalonia;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Commands;
using Beutl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public sealed class GraphEditorKeyFrameViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly GraphEditorViewModel _parent;
    internal readonly ReactivePropertySlim<GraphEditorKeyFrameViewModel?> _previous = new();
    internal GraphEditorKeyFrameViewModel? _next;

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

        Decreasing = StartY.CombineLatest(EndY)
            .Select(x => x.First > x.Second)
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
            .Select(v => new Thickness(0, v.Second - v.First, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsSplineEasing = keyframe.GetObservable(KeyFrame.EasingProperty)
            .Select(v => v is Animation.Easings.SplineEasing)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IObservable<(Vector, Vector)> controlPointObservable = keyframe.GetObservable(KeyFrame.EasingProperty)
            .Select(v =>
            {
                if (v is Animation.Easings.SplineEasing splineEasing)
                {
                    (Vector, Vector) ToVector()
                    {
                        return (new Vector(splineEasing.X1, splineEasing.Y1), new Vector(splineEasing.X2, splineEasing.Y2));
                    }

                    return Observable.FromEventPattern(splineEasing, nameof(Beutl.Animation.Easings.SplineEasing.Changed))
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

    public IKeyFrameAnimation Animation { get; }

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

    public static bool TryConvert(double value, Type type, out object? obj)
    {
        try
        {
            obj = Convert.ChangeType(value, type);
            return true;
        }
        catch
        {
            obj = null;
            return false;
        }
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

    public bool UpdateControlPoint1(Point point)
    {
        if (Model.Easing is Animation.Easings.SplineEasing splineEasing)
        {
            (double x, double y) = CoerceControlPoint(point);

            if (double.IsNormal(x) && double.IsNormal(y))
            {
                splineEasing.X1 = (float)x;
                splineEasing.Y1 = (float)y;
                return true;
            }
        }

        return false;
    }

    public bool UpdateControlPoint2(Point point)
    {
        if (Model.Easing is SplineEasing splineEasing)
        {
            (double x, double y) = CoerceControlPoint(point);

            if (double.IsNormal(x) && double.IsNormal(y))
            {
                splineEasing.X2 = (float)x;
                splineEasing.Y2 = (float)y;
                return true;
            }
        }

        return false;
    }

    public void SubmitControlPoint1(float oldX, float oldY)
    {
        if (Model.Easing is SplineEasing splineEasing)
        {
            var command = new SubmitControlPointCommand((oldX, oldY), (splineEasing.X1, splineEasing.Y1), splineEasing, true);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }

    public void SubmitControlPoint2(float oldX, float oldY)
    {
        if (Model.Easing is SplineEasing splineEasing)
        {
            var command = new SubmitControlPointCommand((oldX, oldY), (splineEasing.X2, splineEasing.Y2), splineEasing, false);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }

    public void SubmitCrossed(TimeSpan timeSpan)
    {
        int rate = _parent.Scene.Parent is Project proj ? proj.GetFrameRate() : 30;
        Model.KeyTime = timeSpan.RoundToRate(rate);
    }

    public void SubmitKeyTimeAndValue(TimeSpan oldKeyTime)
    {
        float scale = _parent.Options.Value.Scale;
        int rate = _parent.Scene.Parent is Project proj ? proj.GetFrameRate() : 30;

        if (TryConvert(EndY.Value / _parent.ScaleY.Value, Animation.Property.PropertyType, out object? obj))
        {
            var command = new SubmitKeyFrameCommand(
                keyframe: Model,
                oldTime: oldKeyTime,
                newTime: Right.Value.ToTimeSpan(scale).RoundToRate(rate),
                oldValue: Model.Value,
                newValue: obj);
            command.DoAndRecord(CommandRecorder.Default);
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
    }

    private sealed class SubmitControlPointCommand : IRecordableCommand
    {
        private readonly (float, float) _oldValue;
        private readonly (float, float) _newValue;
        private readonly SplineEasing _splineEasing;
        private readonly bool _first;

        public SubmitControlPointCommand((float, float) oldValue, (float, float) newValue, SplineEasing splineEasing, bool first)
        {
            _oldValue = oldValue;
            _newValue = newValue;
            _splineEasing = splineEasing;
            _first = first;
        }

        public void Do()
        {
            if (_first)
            {
                _splineEasing.X1 = _newValue.Item1;
                _splineEasing.Y1 = _newValue.Item2;
            }
            else
            {
                _splineEasing.X2 = _newValue.Item1;
                _splineEasing.Y2 = _newValue.Item2;
            }
        }

        public void Redo() => Do();

        public void Undo()
        {
            if (_first)
            {
                _splineEasing.X1 = _oldValue.Item1;
                _splineEasing.Y1 = _oldValue.Item2;
            }
            else
            {
                _splineEasing.X2 = _oldValue.Item1;
                _splineEasing.Y2 = _oldValue.Item2;
            }
        }
    }

    private sealed class SubmitKeyFrameCommand : IRecordableCommand
    {
        private readonly IKeyFrame _keyframe;
        private readonly TimeSpan _oldTime;
        private readonly TimeSpan _newTime;
        private readonly object? _oldValue;
        private readonly object? _newValue;

        public SubmitKeyFrameCommand(IKeyFrame keyframe, TimeSpan oldTime, TimeSpan newTime, object? oldValue, object? newValue)
        {
            _keyframe = keyframe;
            _oldTime = oldTime;
            _newTime = newTime;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _keyframe.Value = _newValue;
            _keyframe.KeyTime = _newTime;
        }

        public void Redo() => Do();

        public void Undo()
        {
            _keyframe.Value = _oldValue;
            _keyframe.KeyTime = _oldTime;
        }
    }
}
