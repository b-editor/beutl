using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Commands;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class GraphEditorViewModel<T>(
    EditViewModel editViewModel,
    KeyFrameAnimation<T> animation,
    Element? element)
    : GraphEditorViewModel(editViewModel, animation, element)
{
    public override void DropEasing(Easing easing, TimeSpan keyTime)
    {
        CommandRecorder recorder = EditorContext.CommandRecorder;
        TimeSpan originalKeyTime = keyTime;
        keyTime = ConvertKeyTime(keyTime);
        Project? proj = Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        TimeSpan threshold = TimeSpan.FromSeconds(1d / rate) * 3;

        IKeyFrame? keyFrame = Animation.KeyFrames.FirstOrDefault(v => Math.Abs(v.KeyTime.Ticks - keyTime.Ticks) <= threshold.Ticks);
        if (keyFrame != null)
        {
            RecordableCommands.Edit(keyFrame, KeyFrame.EasingProperty, easing)
                .WithStoables(GetStorables())
                .DoAndRecord(recorder);
        }
        else
        {
            InsertKeyFrame(easing, originalKeyTime);
        }
    }

    public override void InsertKeyFrame(Easing easing, TimeSpan keyTime)
    {
        keyTime = ConvertKeyTime(keyTime);
        var kfAnimation = (KeyFrameAnimation<T>)Animation;
        if (!kfAnimation.KeyFrames.Any(x => x.KeyTime == keyTime))
        {
            CommandRecorder recorder = EditorContext.CommandRecorder;
            var keyframe = new KeyFrame<T> { Value = kfAnimation.Interpolate(keyTime), Easing = easing, KeyTime = keyTime };

            RecordableCommands.Create(GetStorables())
                .OnDo(() => kfAnimation.KeyFrames.Add(keyframe, out _))
                .OnUndo(() => kfAnimation.KeyFrames.Remove(keyframe))
                .ToCommand()
                .DoAndRecord(recorder);
        }
    }
}

public abstract class GraphEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly GraphEditorViewViewModelFactory[] _factories;
    private readonly ILogger _logger = Log.CreateLogger<GraphEditorViewModel>();
    private bool _editting;

    protected GraphEditorViewModel(EditViewModel editViewModel, IKeyFrameAnimation animation, Element? element)
    {
        EditorContext = editViewModel;
        Element = element;
        Animation = animation;

        UseGlobalClock = ((CoreObject)animation).GetObservable(KeyFrameAnimation.UseGlobalClockProperty)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementMargin = (Element?.GetObservable(Element.StartProperty) ?? Observable.Return<TimeSpan>(default))
            .CombineLatest(editViewModel.Scale)
            .Select(t => new Thickness(t.First.ToPixel(t.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementWidth = (Element?.GetObservable(Element.LengthProperty) ?? Observable.Return<TimeSpan>(default))
            .CombineLatest(editViewModel.Scale)
            .Select(t => t.First.ToPixel(t.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementColor = (Element?.GetObservable(Element.AccentColorProperty) ?? Observable.Return(Beutl.Media.Colors.Transparent))
            .Select(v => (IBrush)new ImmutableSolidColorBrush(v.ToAvalonia()))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Margin = UseGlobalClock.Select(v => !v
                ? Element?.GetObservable(Element.StartProperty)
                    .CombineLatest(Options)
                    .Select(item => new Thickness(item.First.ToPixel(item.Second.Scale), 0, 0, 0))
                : null)
            .Select(v => v ?? Observable.Return<Thickness>(default))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        PanelWidth = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(editViewModel.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SeekBarMargin = editViewModel.CurrentTime
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

    public IReactiveProperty<TimelineOptions> Options => EditorContext.Options;

    public Scene Scene => EditorContext.Scene;

    public Element? Element { get; private set; }

    public ReactivePropertySlim<double> ScaleY { get; } = new(0.5);

    public ReactivePropertySlim<Vector> ScrollOffset { get; } = new();

    public double Padding { get; } = 120;

    public ReactivePropertySlim<double> MinHeight { get; } = new();

    // Zeroの位置
    public ReactivePropertySlim<double> Baseline { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> UseGlobalClock { get; }

    public ReadOnlyReactivePropertySlim<Thickness> Margin { get; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> ElementMargin { get; }

    public ReadOnlyReactivePropertySlim<double> ElementWidth { get; }

    public ReadOnlyReactivePropertySlim<IBrush?> ElementColor { get; }

    public ReactivePropertySlim<GraphEditorViewViewModel?> SelectedView { get; } = new();

    public IKeyFrameAnimation Animation { get; }

    public GraphEditorViewViewModel[] Views { get; }

    public GraphEditorViewViewModelFactory? Factory { get; }

    public EditViewModel EditorContext { get; }

    public ReactiveProperty<bool> Symmetry { get; } = new(true);

    public ReactiveProperty<bool> Asymmetry { get; } = new(false);

    public ReactiveProperty<bool> Separately { get; } = new(false);

    public void BeginEditing()
    {
        _editting = true;
    }

    public void EndEditting()
    {
        _editting = false;
        CalculateMaxHeight();
    }

    public void UpdateUseGlobalClock(bool value)
    {
        CommandRecorder recorder = EditorContext.CommandRecorder;
        RecordableCommands.Edit((ICoreObject)Animation, KeyFrameAnimation.UseGlobalClockProperty, value)
            .WithStoables(GetStorables())
            .DoAndRecord(recorder);
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

    public TimeSpan ConvertKeyTime(TimeSpan globalkeyTime)
    {
        TimeSpan localKeyTime = Element != null ? globalkeyTime - Element.Start : globalkeyTime;
        TimeSpan keyTime = Animation.UseGlobalClock ? globalkeyTime : localKeyTime;

        Project? proj = Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        return keyTime.RoundToRate(rate);
    }

    public abstract void DropEasing(Easing easing, TimeSpan time);

    public abstract void InsertKeyFrame(Easing easing, TimeSpan keyTime);

    public void RemoveKeyFrame(TimeSpan keyTime)
    {
        keyTime = ConvertKeyTime(keyTime);
        IKeyFrame? keyframe = Animation.KeyFrames.FirstOrDefault(x => x.KeyTime == keyTime);
        if (keyframe != null)
        {
            CommandRecorder recorder = EditorContext.CommandRecorder;
            Animation.KeyFrames.BeginRecord<IKeyFrame>()
                .Remove(keyframe)
                .ToCommand(GetStorables())
                .DoAndRecord(recorder);
        }
    }

    public void Paste(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject newJson)
        {
            NotificationService.ShowError(Strings.GraphEditor, "Invalid JSON");
            return;
        }

        try
        {
            CommandRecorder recorder = EditorContext.CommandRecorder;
            JsonObject oldJson = CoreSerializerHelper.SerializeToJsonObject(Animation, typeof(IKeyFrameAnimation));
            KeyFrameAnimation animation = (KeyFrameAnimation)Animation;
            Guid id = animation.Id;
            CoreProperty property = animation.Property;

            RecordableCommands.Create(
                    () =>
                    {
                        CoreSerializerHelper.PopulateFromJsonObject(animation, typeof(IKeyFrameAnimation), newJson);
                        animation.Property = property;
                        animation.Id = id;
                        foreach (IKeyFrame item in animation.KeyFrames)
                        {
                            item.Id = Guid.NewGuid();
                        }
                    },
                    () =>
                    {
                        CoreSerializerHelper.PopulateFromJsonObject(Animation, typeof(IKeyFrameAnimation), oldJson);
                        animation.Property = property;
                        animation.Id = id;
                    },
                    GetStorables())
                .DoAndRecord(recorder);
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Strings.GraphEditor, ex.Message);
            _logger.LogError(ex, "An exception occurred while pasting JSON.");
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

        Element = null;
        GC.SuppressFinalize(this);
    }

    protected ImmutableArray<IStorable?> GetStorables()
    {
        return [Element];
    }
}
