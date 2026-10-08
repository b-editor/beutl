using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using SplineEasing = Beutl.Animation.Easings.SplineEasing;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed class GraphEditorKeyFrameViewModel : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<GraphEditorKeyFrameViewModel>();
    private readonly CompositeDisposable _disposables = [];
    internal readonly ReactivePropertySlim<GraphEditorKeyFrameViewModel?> _previous = new();

    public GraphEditorKeyFrameViewModel(
        IKeyFrame keyframe,
        GraphEditorViewViewModel parent)
    {
        Model = keyframe;
        Parent = parent;
        IsSelected = new ReactivePropertySlim<bool>(parent.IsKeyFrameSelected(keyframe))
            .DisposeWith(_disposables);

        EndY = Model.ObserveProperty(x => x.Value)
            .Select(Parent.ConvertToDouble)
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
            .Select(x => x?.Right ?? Parent.Parent.Margin.Select(x => -x.Left))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Right = keyframe.GetObservable(KeyFrame.KeyTimeProperty)
            .CombineLatest(parent.Parent.Options)
            .Select(item => item.First.TimeToPixel(item.Second.Scale))
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

        ShowControlPoint1 = _previous.Select(x => (IObservable<bool>?)x?.IsSelected ?? Observable.ReturnThenNever(false))
            .Switch().CombineLatest(IsSplineEasing, (selected, spline) => selected && spline)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        ShowControlPoint2 = IsSelected.CombineLatest(IsSplineEasing, _previous,
                (selected, spline, previous) => selected && spline && previous != null)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);

        IObservable<(Vector, Vector)> controlPointObservable = keyframe.GetObservable(KeyFrame.EasingProperty)
            .Select(v =>
            {
                if (v is SplineEasing splineEasing)
                {
                    (Vector, Vector) ToVector()
                    {
                        return (new Vector(splineEasing.X1, splineEasing.Y1),
                            new Vector(splineEasing.X2, splineEasing.Y2));
                    }

                    return Observable.FromEventPattern(splineEasing, nameof(SplineEasing.Changed))
                        .Select(_ => ToVector())
                        .Publish(ToVector())
                        .RefCount();
                }
                else
                {
                    return Observable.ReturnThenNever<(Vector, Vector)>(default);
                }
            })
            .Switch();

        ControlPoint1 = CreateControlPoint(controlPointObservable.Select(v => v.Item1));

        ControlPoint2 = CreateControlPoint(controlPointObservable.Select(v => v.Item2));

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

        keyframe.GetObservable(KeyFrame.EasingProperty).Skip(1)
            .Subscribe(_ => Parent.NotifyGraphChanged()).DisposeWith(_disposables);

        CopyCommand = new AsyncReactiveCommand()
            .WithSubscribe(CopyAsync)
            .DisposeWith(_disposables);

        PasteCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => PasteAsync())
            .DisposeWith(_disposables);

        RemoveCommand = new ReactiveCommand()
            .WithSubscribe(Remove)
            .DisposeWith(_disposables);
    }

    // Maps a normalized easing control point into this segment's box, flipping it for a decreasing segment.
    private ReadOnlyReactivePropertySlim<Point> CreateControlPoint(IObservable<Vector> controlPoint)
    {
        return controlPoint
            .CombineLatest(Decreasing)
            .Select(v => v.First.WithY(v.Second ? v.First.Y : 1 - v.First.Y))
            .CombineLatest(Width, Height, (pt, w, h) => (Point)Vector.Multiply(pt, new Vector(w, h)))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public GraphEditorViewViewModel Parent { get; }

    public IKeyFrame Model { get; }

    public ReactivePropertySlim<bool> IsSelected { get; }

    public ReadOnlyReactivePropertySlim<IBrush?> Stroke => Parent.Stroke;

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
    public ReadOnlyReactivePropertySlim<bool> ShowControlPoint1 { get; }
    public ReadOnlyReactivePropertySlim<bool> ShowControlPoint2 { get; }

    public ReadOnlyReactivePropertySlim<Point> ControlPoint1 { get; }

    public ReadOnlyReactivePropertySlim<Point> ControlPoint2 { get; }

    public ReadOnlyReactivePropertySlim<Point> LeftBottom { get; }

    public ReadOnlyReactivePropertySlim<Point> RightTop { get; }

    public AsyncReactiveCommand CopyCommand { get; set; }

    public AsyncReactiveCommand PasteCommand { get; set; }

    public ReactiveCommand RemoveCommand { get; }

    public void SetPrevious(GraphEditorKeyFrameViewModel? previous)
    {
        _previous.Value = previous;
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

    private async Task CopyAsync()
    {
        if (IsSelected.Value)
        {
            await Parent.Parent.CopySelectionAsync();
            return;
        }
        await KeyFrameClipboardCommands.CopyAsync(Model, BeutlDataFormats.KeyFrame, ex =>
        {
            _logger.LogError(ex, "Failed to copy keyframe");
            NotificationService.ShowError(Strings.Copy, MessageStrings.FailedToCopyKeyframe);
        });
    }

    internal Task PasteAsync(IClipboard? clipboard = null) => Parent.Parent.PasteSelectionAsync(clipboard, Model);

    private void Remove()
    {
        if (IsSelected.Value)
        {
            var selected = Parent.KeyFrames.Where(x => x.IsSelected.Value).Select(x => x.Model).ToArray();
            Parent.Parent.DeleteKeyFrames(selected);
            return;
        }
        AnimationOperations.RemoveKeyFrame(
            animation: Parent.Parent.Animation,
            keyframe: Model,
            logger: _logger);
        Parent.Parent.HistoryManager.Commit(CommandNames.RemoveKeyFrame);
    }
}
