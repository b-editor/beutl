using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Helpers;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Models;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using SplineEasing = Beutl.Animation.Easings.SplineEasing;

namespace Beutl.ViewModels;

public sealed class GraphEditorKeyFrameViewModel : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<GraphEditorKeyFrameViewModel>();
    private readonly CompositeDisposable _disposables = [];
    internal readonly ReactivePropertySlim<GraphEditorKeyFrameViewModel?> _previous = new();
    internal GraphEditorKeyFrameViewModel? _next;

    public GraphEditorKeyFrameViewModel(
        IKeyFrame keyframe,
        GraphEditorViewViewModel parent)
    {
        Model = keyframe;
        Parent = parent;

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

        CopyCommand = new AsyncReactiveCommand()
            .WithSubscribe(CopyAsync)
            .DisposeWith(_disposables);

        PasteCommand = new AsyncReactiveCommand()
            .WithSubscribe(PasteAsync)
            .DisposeWith(_disposables);

        RemoveCommand = new ReactiveCommand()
            .WithSubscribe(Remove)
            .DisposeWith(_disposables);
    }

    public GraphEditorViewViewModel Parent { get; }

    public IKeyFrame Model { get; }

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
        if (previous != null)
        {
            previous._next = this;
        }
    }

    public void SetLast()
    {
        _next = null;
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

    public void SubmitCrossed(TimeSpan timeSpan)
    {
        int rate = Parent.Parent.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        Model.KeyTime = timeSpan.RoundToRate(rate);
    }

    public void CommitKeyTimeAndValue()
    {
        GraphEditorViewModel parent2 = Parent.Parent;
        HistoryManager history = parent2.EditorContext.HistoryManager;
        IKeyFrameAnimation animation = parent2.Animation;

        float scale = parent2.Options.Value.Scale;
        int rate = parent2.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

        if (Parent.TryConvertFromDouble(Model.Value, EndY.Value / parent2.ScaleY.Value, animation.ValueType,
                out object? obj))
        {
            Model.Value = obj;
            Model.KeyTime = Right.Value.ToTimeSpan(scale).RoundToRate(rate);
        }
        else
        {
            Model.KeyTime = Right.Value.ToTimeSpan(scale).RoundToRate(rate);
        }

        history.Commit(CommandNames.EditKeyFrame);

        EndY.Value = Parent.ConvertToDouble(Model.Value) * parent2.ScaleY.Value;
        Right.Value = Model.KeyTime.ToPixel(Parent.Parent.Options.Value.Scale);
    }

    public void UpdateKeyTimeAndValue()
    {
        GraphEditorViewModel parent2 = Parent.Parent;
        IKeyFrameAnimation animation = parent2.Animation;

        float scale = parent2.Options.Value.Scale;
        int rate = parent2.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

        if (Parent.TryConvertFromDouble(Model.Value, EndY.Value / parent2.ScaleY.Value, animation.ValueType,
                out object? obj))
        {
            Model.Value = obj;
            Model.KeyTime = Right.Value.ToTimeSpan(scale).RoundToRate(rate);

            EndY.Value = Parent.ConvertToDouble(Model.Value) * parent2.ScaleY.Value;
        }
        else
        {
            Model.KeyTime = Right.Value.ToTimeSpan(scale).RoundToRate(rate);
        }

        Right.Value = Model.KeyTime.ToPixel(Parent.Parent.Options.Value.Scale);
    }

    private async Task CopyAsync()
    {
        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        try
        {
            var data = new DataTransfer();
            ObjectRegenerator.Regenerate(Model, out string json);
            data.Add(DataTransferItem.CreateText(json));
            data.Add(DataTransferItem.Create(BeutlDataFormats.KeyFrame, json));

            await clipboard.SetDataAsync(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy keyframe");
            NotificationService.ShowError(Strings.Copy, Strings.FailedToCopyKeyframe);
        }
    }

    private async Task PasteAsync()
    {
        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        try
        {
            if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrame) is { } json
                && JsonNode.Parse(json) is JsonObject jsonObj)
            {
                if (!jsonObj.TryGetDiscriminator(out Type? type))
                {
                    NotificationService.ShowWarning(Strings.Paste, Strings.InvalidKeyframeDataFormat_MissingType);
                    return;
                }

                if (!type.IsAssignableTo(typeof(KeyFrame)))
                {
                    NotificationService.ShowWarning(Strings.Paste, Strings.InvalidKeyframeDataFormat_TypeIsNotKeyFrame);
                    return;
                }

                KeyFrame newKeyFrame = (KeyFrame)Activator.CreateInstance(type)!;
                CoreSerializer.PopulateFromJsonObject(newKeyFrame, jsonObj);
                HistoryManager history = Parent.Parent.EditorContext.HistoryManager;

                if (type.GenericTypeArguments[0] != Parent.Parent.Animation.ValueType)
                {
                    // イージングのみ変更
                    Model.Easing = newKeyFrame.Easing;
                    history.Commit(CommandNames.PasteKeyFrame);
                    NotificationService.ShowWarning(Strings.GraphEditor,
                        Strings.KeyframePropertyTypeMismatch_EasingApplied);
                }
                else
                {
                    newKeyFrame.KeyTime = Model.KeyTime;
                    int index = Parent.Parent.Animation.KeyFrames.IndexOf(Model);
                    Parent.Parent.Animation.KeyFrames.Insert(index, (IKeyFrame)newKeyFrame);
                    history.Commit(CommandNames.PasteKeyFrame);
                }

                return;
            }

            NotificationService.ShowWarning(Strings.Paste, Strings.InvalidKeyframeDataFormat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste keyframe");
            NotificationService.ShowError(Strings.Paste, Strings.FailedToPasteKeyframe);
        }
    }

    private void Remove()
    {
        AnimationOperations.RemoveKeyFrame(
            animation: Parent.Parent.Animation,
            keyframe: Model,
            logger: _logger);
        Parent.Parent.EditorContext.HistoryManager.Commit(CommandNames.RemoveKeyFrame);
    }
}
