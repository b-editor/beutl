using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Helpers;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
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
        _logger.LogInformation("Dropping easing at key time {KeyTime}", keyTime);
        var history = EditorContext.HistoryManager;
        TimeSpan originalKeyTime = keyTime;
        keyTime = ConvertKeyTime(keyTime);
        Project? proj = Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        TimeSpan threshold = TimeSpan.FromSeconds(1d / rate) * 3;

        IKeyFrame? keyFrame =
            Animation.KeyFrames.FirstOrDefault(v => Math.Abs(v.KeyTime.Ticks - keyTime.Ticks) <= threshold.Ticks);
        if (keyFrame != null)
        {
            _logger.LogInformation("Editing existing key frame at {KeyTime}", keyTime);
            keyFrame.Easing = easing;
            history.Commit(CommandNames.ChangeEasing);
        }
        else
        {
            InsertKeyFrame(easing, originalKeyTime);
        }
    }

    public override void InsertKeyFrame(Easing easing, TimeSpan keyTime)
    {
        AnimationOperations.InsertKeyFrame(
            animation: (KeyFrameAnimation<T>)Animation,
            easing: easing,
            keyTime: keyTime,
            logger: _logger);
        EditorContext.HistoryManager.Commit(CommandNames.InsertKeyFrame);
    }
}

public abstract class GraphEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly GraphEditorViewViewModelFactory[] _factories;
    protected readonly ILogger _logger = Log.CreateLogger<GraphEditorViewModel>();
    private bool _editting;
    private TimeSpan _pointerPosition;

    protected GraphEditorViewModel(EditViewModel editViewModel, IKeyFrameAnimation animation, Element? element)
    {
        _logger.LogInformation("Initializing GraphEditorViewModel");
        EditorContext = editViewModel;
        Element = element;
        Animation = animation;

        UseGlobalClock = ((CoreObject)animation).GetObservable(KeyFrameAnimation.UseGlobalClockProperty)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementMargin = (Element?.GetObservable(Element.StartProperty) ?? Observable.ReturnThenNever<TimeSpan>(default))
            .CombineLatest(editViewModel.Scale)
            .Select(t => new Thickness(t.First.ToPixel(t.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementWidth = (Element?.GetObservable(Element.LengthProperty) ?? Observable.ReturnThenNever<TimeSpan>(default))
            .CombineLatest(editViewModel.Scale)
            .Select(t => t.First.ToPixel(t.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementColor = (Element?.GetObservable(Element.AccentColorProperty) ??
                        Observable.ReturnThenNever(Beutl.Media.Colors.Transparent))
            .Select(v => (IBrush)new ImmutableSolidColorBrush(v.ToAvalonia()))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Margin = UseGlobalClock.Select(v => !v
                ? Element?.GetObservable(Element.StartProperty)
                    .CombineLatest(Options)
                    .Select(item => new Thickness(item.First.ToPixel(item.Second.Scale), 0, 0, 0))
                : null)
            .Select(v => v ?? Observable.ReturnThenNever<Thickness>(default))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SeekBarMargin = editViewModel.CurrentTime
            .CombineLatest(editViewModel.Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        StartingBarMargin = Scene.GetObservable(Scene.StartProperty)
            .CombineLatest(editViewModel.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        EndingBarMargin = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(editViewModel.Scale, StartingBarMargin)
            .Select(item => item.First.ToPixel(item.Second) + item.Third.Left)
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        PanelWidth = editViewModel.MaximumTime
            .CombineLatest(
                Scene.GetObservable(Scene.DurationProperty),
                Scene.GetObservable(Scene.StartProperty),
                editViewModel.CurrentTime)
            .Select(i => TimeSpan.FromTicks(
                Math.Max(
                    Math.Max(i.First.Ticks, i.Second.Ticks + i.Third.Ticks),
                    i.Fourth.Ticks)))
            .CombineLatest(editViewModel.Scale)
            .Select(i => i.First.ToPixel(i.Second) + 500)
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

        CopyAllKeyFramesCommand = new AsyncReactiveCommand()
            .WithSubscribe(CopyAllKeyFramesAsync)
            .DisposeWith(_disposables);

        PasteKeyFrameAtCurrentPositionCommand = new AsyncReactiveCommand()
            .WithSubscribe(async () => await PasteKeyFrameAtPositionAsync(_pointerPosition))
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

    public ReadOnlyReactivePropertySlim<Thickness> StartingBarMargin { get; }

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

    public AsyncReactiveCommand CopyAllKeyFramesCommand { get; }

    public AsyncReactiveCommand PasteKeyFrameAtCurrentPositionCommand { get; }

    public void UpdatePointerPosition(double positionX)
    {
        float scale = Options.Value.Scale;
        _pointerPosition = positionX.ToTimeSpan(scale);
    }

    public void BeginEditing()
    {
        _logger.LogInformation("Begin editing");
        _editting = true;
    }

    public void EndEditting()
    {
        _logger.LogInformation("End editing");
        EditorContext.HistoryManager.Commit(CommandNames.EditKeyFrame);
        _editting = false;
        CalculateMaxHeight();
    }

    public void UpdateUseGlobalClock(bool value)
    {
        _logger.LogInformation("Updating UseGlobalClock to {Value}", value);
        var history = EditorContext.HistoryManager;
        ((KeyFrameAnimation)Animation).UseGlobalClock = value;
        history.Commit(CommandNames.ChangeUseGlobalClock);
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
        _logger.LogInformation("Converting key time {GlobalKeyTime}", globalkeyTime);
        TimeSpan localKeyTime = Element != null ? globalkeyTime - Element.Start : globalkeyTime;
        TimeSpan keyTime = Animation.UseGlobalClock ? globalkeyTime : localKeyTime;

        Project? proj = Scene.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        return keyTime.RoundToRate(rate);
    }

    public abstract void DropEasing(Easing easing, TimeSpan time);

    public abstract void InsertKeyFrame(Easing easing, TimeSpan keyTime);

    public void Dispose()
    {
        _logger.LogInformation("Disposing GraphEditorViewModel");
        _disposables.Dispose();
        foreach (GraphEditorViewViewModel item in Views)
        {
            item.VerticalRangeChanged -= OnItemVerticalRangeChanged;
            item.Dispose();
        }

        Element = null;
        GC.SuppressFinalize(this);
    }

    private async Task CopyAllKeyFramesAsync()
    {
        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        try
        {
            ObjectRegenerator.Regenerate(Animation, out string json);

            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(json));
            data.Add(DataTransferItem.Create(BeutlDataFormats.KeyFrameAnimation, json));

            await clipboard.SetDataAsync(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy all keyframes");
            NotificationService.ShowError(Strings.Copy, Strings.FailedToCopyAnimation);
        }
    }

    private async Task PasteKeyFrameAtPositionAsync(TimeSpan pointerPosition)
    {
        IClipboard? clipboard = App.GetClipboard();
        if (clipboard == null) return;

        try
        {
            if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrame) is { } keyFrameJson)
            {
                PasteKeyFrame(keyFrameJson, pointerPosition);
                return;
            }
            else if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrameAnimation) is { } keyFrameAnimationJson)
            {
                PasteAnimation(keyFrameAnimationJson);
                return;
            }

            NotificationService.ShowWarning(Strings.Paste, Strings.InvalidKeyframeDataFormat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste keyframe at position");
            NotificationService.ShowError(Strings.Paste, Strings.FailedToPasteKeyframe);
        }
    }

    private void PasteAnimation(string json)
    {
        _logger.LogInformation("Pasting JSON");
        if (JsonNode.Parse(json) is not JsonObject newJson)
        {
            _logger.LogError("Invalid JSON");
            NotificationService.ShowError(Strings.GraphEditor, Strings.InvalidJson);
            return;
        }

        if (!newJson.TryGetDiscriminator(out Type? discriminator))
        {
            _logger.LogError("Invalid JSON: missing $type");
            NotificationService.ShowError(Strings.GraphEditor, Strings.InvalidJSON_MissingType);
            return;
        }

        if (!discriminator.IsAssignableTo(typeof(IKeyFrameAnimation)))
        {
            _logger.LogError("Invalid JSON: $type is not a KeyFrameAnimation");
            NotificationService.ShowError(Strings.GraphEditor, Strings.InvalidJSON_TypeIsNotKeyFrameAnimation);
            return;
        }

        try
        {
            HistoryManager history = EditorContext.HistoryManager;
            KeyFrameAnimation animation = (KeyFrameAnimation)Animation;

            if (discriminator.GenericTypeArguments[0] != animation.ValueType)
            {
                _logger.LogError("The property type of the pasted animation does not match.");
                NotificationService.ShowError(Strings.GraphEditor, string.Format(Strings.AnimationPropertyTypeMismatch, animation.ValueType.Name, discriminator.GenericTypeArguments[0].Name));
                return;
            }

            Guid id = animation.Id;
            CoreSerializer.PopulateFromJsonObject(animation, newJson);
            animation.Id = id;
            foreach (IKeyFrame item in animation.KeyFrames)
            {
                item.Id = Guid.NewGuid();
            }
            history.Commit(CommandNames.PasteAnimation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while pasting JSON.");
            NotificationService.ShowError(Strings.GraphEditor, ex.Message);
        }
    }

    private void PasteKeyFrame(string json, TimeSpan pointerPosition)
    {
        _logger.LogInformation("Pasting JSON");
        if (JsonNode.Parse(json) is not JsonObject newJson)
        {
            _logger.LogError("Invalid JSON");
            NotificationService.ShowError(Strings.GraphEditor, Strings.InvalidJson);
            return;
        }

        if (!newJson.TryGetDiscriminator(out Type? discriminator))
        {
            _logger.LogError("Invalid JSON: missing $type");
            NotificationService.ShowError(Strings.GraphEditor, Strings.InvalidJSON_MissingType);
            return;
        }

        if (!discriminator.IsAssignableTo(typeof(KeyFrame)))
        {
            _logger.LogError("Invalid JSON: $type is not a KeyFrame");
            NotificationService.ShowError(Strings.GraphEditor, Strings.InvalidJSON_TypeIsNotKeyFrame);
            return;
        }

        try
        {
            HistoryManager history = EditorContext.HistoryManager;
            KeyFrameAnimation animation = (KeyFrameAnimation)Animation;

            KeyFrame newKeyFrame = (KeyFrame)Activator.CreateInstance(discriminator)!;
            CoreSerializer.PopulateFromJsonObject(newKeyFrame, newJson);

            if (discriminator.GenericTypeArguments[0] != animation.ValueType)
            {
                InsertKeyFrame(newKeyFrame.Easing, pointerPosition);
                NotificationService.ShowWarning(Strings.GraphEditor, Strings.KeyframePropertyTypeMismatch_EasingApplied);
                return;
            }

            var keyTime = ConvertKeyTime(pointerPosition);
            if (animation.KeyFrames.FirstOrDefault(k => k.KeyTime == keyTime) is { } existingKeyFrame)
            {
                // イージングと値を変更
                existingKeyFrame.Easing = newKeyFrame.Easing;
                existingKeyFrame.Value = ((IKeyFrame)newKeyFrame).Value;
                history.Commit(CommandNames.PasteKeyFrame);
                NotificationService.ShowWarning(Strings.GraphEditor, Strings.KeyframeExistsAtPastePosition);
            }
            else
            {
                newKeyFrame.KeyTime = keyTime;
                animation.KeyFrames.Add((IKeyFrame)newKeyFrame, out _);
                history.Commit(CommandNames.PasteKeyFrame);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while pasting JSON.");
            NotificationService.ShowError(Strings.GraphEditor, ex.Message);
        }
    }
}
