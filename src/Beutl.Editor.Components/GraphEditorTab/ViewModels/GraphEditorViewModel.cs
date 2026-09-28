using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed class GraphEditorViewModel<T>(
    IEditorContext editorContext,
    KeyFrameAnimation<T> animation,
    Element? element)
    : GraphEditorViewModel(editorContext, animation, element)
{
    public override void DropEasing(Easing easing, TimeSpan keyTime)
    {
        _logger.LogInformation("Dropping easing at key time {KeyTime}", keyTime);
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
            HistoryManager.Commit(CommandNames.ChangeEasing);
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
        HistoryManager.Commit(CommandNames.InsertKeyFrame);
    }
}

public abstract partial class GraphEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly GraphEditorViewViewModelFactory[] _factories;
    private readonly IEditorClock _editorClock;
    protected readonly ILogger _logger = Log.CreateLogger<GraphEditorViewModel>();
    private bool _editting;
    private bool _rangeUpdatePending;
    private bool _disposed;
    private TimeSpan _pointerPosition;

    protected GraphEditorViewModel(IEditorContext editorContext, IKeyFrameAnimation animation, Element? element)
    {
        _logger.LogInformation("Initializing GraphEditorViewModel");
        EditorContext = editorContext;
        Element = element;
        Animation = animation;

        var timelineOptions = editorContext.GetRequiredService<ITimelineOptionsProvider>();
        _editorClock = editorContext.GetRequiredService<IEditorClock>();
        Options = timelineOptions.Options;
        Scene = timelineOptions.Scene;
        HistoryManager = editorContext.GetRequiredService<HistoryManager>();

        UseGlobalClock = ((CoreObject)animation).GetObservable(KeyFrameAnimation.UseGlobalClockProperty)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementMargin = (Element?.GetObservable(Element.StartProperty) ?? Observable.ReturnThenNever<TimeSpan>(default))
            .CombineLatest(timelineOptions.Scale)
            .Select(t => new Thickness(t.First.TimeToPixel(t.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementWidth = (Element?.GetObservable(Element.LengthProperty) ?? Observable.ReturnThenNever<TimeSpan>(default))
            .CombineLatest(timelineOptions.Scale)
            .Select(t => t.First.TimeToPixel(t.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementColor = (Element?.GetObservable(Element.AccentColorProperty) ??
                        Observable.ReturnThenNever(Media.Colors.Transparent))
            .Select(v => (IBrush)new ImmutableSolidColorBrush(v.ToAvaColor()))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Margin = UseGlobalClock.Select(v => !v
                ? Element?.GetObservable(Element.StartProperty)
                    .CombineLatest(Options)
                    .Select(item => new Thickness(item.First.TimeToPixel(item.Second.Scale), 0, 0, 0))
                : null)
            .Select(v => v ?? Observable.ReturnThenNever<Thickness>(default))
            .Switch()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SeekBarMargin = _editorClock.CurrentTime
            .CombineLatest(timelineOptions.Scale)
            .Select(item => new Thickness(item.First.TimeToPixel(item.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        StartingBarMargin = Scene.GetObservable(Scene.StartProperty)
            .CombineLatest(timelineOptions.Scale)
            .Select(item => item.First.TimeToPixel(item.Second))
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        EndingBarMargin = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(timelineOptions.Scale, StartingBarMargin)
            .Select(item => item.First.TimeToPixel(item.Second) + item.Third.Left)
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        PanelWidth = _editorClock.MaximumTime
            .CombineLatest(
                Scene.GetObservable(Scene.DurationProperty),
                Scene.GetObservable(Scene.StartProperty),
                _editorClock.CurrentTime)
            .Select(i => TimeSpan.FromTicks(
                Math.Max(
                    Math.Max(i.First.Ticks, i.Second.Ticks + i.Third.Ticks),
                    i.Fourth.Ticks)))
            .CombineLatest(timelineOptions.Scale)
            .Select(i => i.First.TimeToPixel(i.Second) + 500)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        _factories = GraphEditorViewViewModelFactory.GetFactory(this).ToArray();
        Factory = _factories.FirstOrDefault();
        Views = GraphEditorViewViewModelFactory.CreateViews(this, Factory);
        foreach (GraphEditorViewViewModel item in Views)
        {
            item.VerticalRangeChanged += OnItemVerticalRangeChanged;
            item.SelectionChanged += OnClipboardSelectionChanged;
        }

        SelectedView.Value = Views.FirstOrDefault();
        SelectedView.Skip(1).Subscribe(_ => _clipboardContextVersion++).DisposeWith(_disposables);
        HasSelection = SelectedView.Select(view => (IObservable<int>?)view?.SelectionCount ?? Observable.ReturnThenNever(0))
            .Switch().Select(count => count > 0).ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);

        CalculateMaxHeight();

        timelineOptions.Offset.Subscribe(v => ScrollOffset.Value = ScrollOffset.Value.WithX(v.X))
            .DisposeWith(_disposables);

        CopyAllKeyFramesCommand = new AsyncReactiveCommand()
            .WithSubscribe(CopyAllKeyFramesAsync)
            .DisposeWith(_disposables);

        PasteKeyFrameAtCurrentPositionCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => PasteKeyFrameAtPositionAsync(_pointerPosition))
            .DisposeWith(_disposables);
    }

    public IReactiveProperty<TimelineOptions> Options { get; }

    public Scene Scene { get; }

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

    public ReadOnlyReactivePropertySlim<bool> HasSelection { get; }

    public IKeyFrameAnimation Animation { get; }

    public GraphEditorViewViewModel[] Views { get; }

    public GraphEditorViewViewModelFactory? Factory { get; }

    public IEditorContext EditorContext { get; }

    public HistoryManager HistoryManager { get; }

    public IReactiveProperty<TimeSpan> CurrentTime => _editorClock.CurrentTime;

    public ReactivePropertySlim<bool> IsSpeedGraph { get; } = new();

    public ReactivePropertySlim<bool> AutoZoomHeight { get; } = new();

    public ReactivePropertySlim<bool> Snap { get; } = new(true);

    public ReactivePropertySlim<bool> ShowTransformBox { get; } = new(true);

    public bool IsEditing => _editting;

    internal bool IsDisposed => _disposed;

    public ReactiveProperty<bool> Symmetry { get; } = new(false);

    public ReactiveProperty<bool> Asymmetry { get; } = new(true);

    public ReactiveProperty<bool> Separately { get; } = new(false);

    public AsyncReactiveCommand CopyAllKeyFramesCommand { get; }

    public AsyncReactiveCommand PasteKeyFrameAtCurrentPositionCommand { get; }

    public void UpdatePointerPosition(double positionX)
    {
        float scale = Options.Value.Scale;
        _pointerPosition = positionX.PixelToTimeSpan(scale);
    }

    internal void DeleteKeyFrames(IEnumerable<IKeyFrame> keyFrames)
    {
        var selected = keyFrames.Where(Animation.KeyFrames.Contains).ToArray();
        if (selected.Length == 0) return;
        var selection = Views.Select(view => (view.Name, Keys: view.KeyFrames.Where(key => key.IsSelected.Value)
            .Select(key => key.Model).ToArray())).ToArray();
        var owner = new WeakReference<GraphEditorViewModel>(this);
        HistoryManager.ExecuteInTransaction(() =>
        {
            // Recorded first so Undo restores the selection after reinstating the removed keys.
            HistoryManager.Record(() => { }, () =>
            {
                if (!owner.TryGetTarget(out var model) || model._disposed) return;
                foreach (var (name, keys) in selection)
                    model.Views.FirstOrDefault(view => view.Name == name)?.SetSelection(keys);
            });
            foreach (var key in selected.Reverse())
                AnimationOperations.RemoveKeyFrame(Animation, key, _logger);
        }, CommandNames.RemoveKeyFrame);
    }

    public void BeginEditing()
    {
        _logger.LogInformation("Begin editing");
        _editting = true;
    }

    public void EndEditting()
    {
        _logger.LogInformation("End editing");
        HistoryManager.Commit(CommandNames.EditKeyFrame);
        _editting = false;
        CalculateMaxHeight();
    }

    public void ToggleUseGlobalClock()
    {
        var newValue = !UseGlobalClock.Value;
        _logger.LogInformation("Updating UseGlobalClock to {Value}", newValue);
        ((KeyFrameAnimation)Animation).UseGlobalClock = newValue;
        HistoryManager.Commit(CommandNames.ChangeUseGlobalClock);
    }

    private void OnItemVerticalRangeChanged(object? sender, EventArgs e)
    {
        if (_rangeUpdatePending || _disposed) return;
        _rangeUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rangeUpdatePending = false;
            if (!_disposed) CalculateMaxHeight();
        });
    }

    internal void RefreshVerticalRange() => CalculateMaxHeight();

    private void CalculateMaxHeight()
    {
        double max = 0d;
        double min = 0d;
        foreach (GraphEditorViewViewModel view in Views)
        {
            if (IsSpeedGraph.Value)
            {
                double low = 0, high = 0;
                view.GetGraphRange(ref low, ref high);
                min = Math.Min(min, low * ScaleY.Value);
                max = Math.Max(max, high * ScaleY.Value);
            }
            else view.GetVerticalRange(ref min, ref max);
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
        _disposed = true;
        SetClipboardViewContext(null);
        _disposables.Dispose();
        foreach (GraphEditorViewViewModel item in Views)
        {
            item.VerticalRangeChanged -= OnItemVerticalRangeChanged;
            item.SelectionChanged -= OnClipboardSelectionChanged;
            item.Dispose();
        }

        Element = null;
        GC.SuppressFinalize(this);
    }

    private async Task CopyAllKeyFramesAsync()
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
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
            NotificationService.ShowError(Strings.Copy, MessageStrings.FailedToCopyAnimation);
        }
    }

    internal async Task PasteKeyFrameAtPositionAsync(TimeSpan pointerPosition, IClipboard? clipboard = null)
    {
        clipboard ??= ClipboardHelper.GetClipboard();
        if (!IsClipboardContextActive || clipboard == null || SelectedView.Value == null) return;
        var canPaste = CapturePasteContext();

        try
        {
            if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrame) is { } keyFrameJson)
            {
                if (!canPaste()) return;
                PasteKeyFrame(keyFrameJson, pointerPosition);
                return;
            }
            else if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrameSelection) is { } selectionJson)
            {
                if (!canPaste()) return;
                PasteSelection(selectionJson, ConvertKeyTime(pointerPosition));
                return;
            }
            else if (await clipboard.TryGetValueAsync(BeutlDataFormats.KeyFrameAnimation) is { } keyFrameAnimationJson)
            {
                if (!canPaste()) return;
                PasteAnimation(keyFrameAnimationJson);
                return;
            }

            if (canPaste()) NotificationService.ShowWarning(Strings.Paste, MessageStrings.InvalidKeyframeDataFormat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste keyframe at position");
            if (canPaste()) NotificationService.ShowError(Strings.Paste, MessageStrings.FailedToPasteKeyframe);
        }
    }

    private void PasteAnimation(string json)
    {
        _logger.LogInformation("Pasting JSON");
        KeyFrameAnimation animation = (KeyFrameAnimation)Animation;
        IKeyFrameClipboardService service = EditorContext.GetRequiredService<IKeyFrameClipboardService>();
        KeyFrameAnimationPasteOutcome outcome = service.PasteAnimation(animation, json);

        switch (outcome)
        {
            case KeyFrameAnimationPasteOutcome.Pasted:
                break;
            case KeyFrameAnimationPasteOutcome.InvalidJson:
                _logger.LogError("Invalid JSON");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJson);
                break;
            case KeyFrameAnimationPasteOutcome.MissingType:
                _logger.LogError("Invalid JSON: missing $type");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJSON_MissingType);
                break;
            case KeyFrameAnimationPasteOutcome.TypeIsNotKeyFrameAnimation:
                _logger.LogError("Invalid JSON: $type is not a KeyFrameAnimation");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJSON_TypeIsNotKeyFrameAnimation);
                break;
            case KeyFrameAnimationPasteOutcome.GenericTypeMismatch:
                _logger.LogError("The property type of the pasted animation does not match.");
                NotificationService.ShowError(
                    Strings.GraphEditor,
                    string.Format(MessageStrings.AnimationPropertyTypeMismatch, animation.ValueType.Name, "?"));
                break;
            case KeyFrameAnimationPasteOutcome.UnexpectedError:
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.FailedToPasteKeyframe);
                break;
        }
    }

    private void PasteKeyFrame(string json, TimeSpan pointerPosition)
    {
        _logger.LogInformation("Pasting JSON");
        KeyFrameAnimation animation = (KeyFrameAnimation)Animation;
        TimeSpan keyTime = ConvertKeyTime(pointerPosition);
        IKeyFrameClipboardService service = EditorContext.GetRequiredService<IKeyFrameClipboardService>();
        KeyFramePasteResult result = service.PasteKeyFrame(animation, json, keyTime);

        switch (result.Outcome)
        {
            case KeyFramePasteOutcome.Inserted:
                break;
            case KeyFramePasteOutcome.ReplacedExisting:
                NotificationService.ShowWarning(Strings.GraphEditor, MessageStrings.KeyframeExistsAtPastePosition);
                break;
            case KeyFramePasteOutcome.GenericTypeMismatch when result.EasingForFallback is { } easing:
                // Type mismatch: insert a fresh keyframe via the View's typed path,
                // carrying over only the clipboard's easing.
                InsertKeyFrame(easing, pointerPosition);
                NotificationService.ShowWarning(Strings.GraphEditor, MessageStrings.KeyframePropertyTypeMismatch_EasingApplied);
                break;
            case KeyFramePasteOutcome.InvalidJson:
                _logger.LogError("Invalid JSON");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJson);
                break;
            case KeyFramePasteOutcome.MissingType:
                _logger.LogError("Invalid JSON: missing $type");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJSON_MissingType);
                break;
            case KeyFramePasteOutcome.TypeIsNotKeyFrame:
                _logger.LogError("Invalid JSON: $type is not a KeyFrame");
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.InvalidJSON_TypeIsNotKeyFrame);
                break;
            case KeyFramePasteOutcome.UnexpectedError:
                NotificationService.ShowError(Strings.GraphEditor, MessageStrings.FailedToPasteKeyframe);
                break;
        }
    }
}
