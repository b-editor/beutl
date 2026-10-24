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
using Beutl.Engine;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed partial class GraphEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<bool> _canEdit = new(true);
    private readonly IEditorClock _editorClock;
    private readonly EngineObject? _animationOwner;
    private readonly ILogger _logger = Log.CreateLogger<GraphEditorViewModel>();
    private bool _editting;
    private bool _rangeUpdatePending;
    private bool _disposed;
    private MediaTime _pointerPosition;

    public GraphEditorViewModel(IEditorContext editorContext, IKeyFrameAnimation animation, Element? element)
        : this(editorContext, animation, element, null)
    {
    }

    internal GraphEditorViewModel(IEditorContext editorContext, IKeyFrameAnimation animation, Element? element,
        EngineObject? animationOwner)
    {
        _logger.LogInformation("Initializing GraphEditorViewModel");
        EditorContext = editorContext;
        Element = element;
        Animation = animation;
        _animationOwner = animationOwner;

        var timelineOptions = editorContext.GetRequiredService<ITimelineOptionsProvider>();
        _editorClock = editorContext.GetRequiredService<IEditorClock>();
        Options = timelineOptions.Options;
        Scene = timelineOptions.Scene;
        HistoryManager = editorContext.GetRequiredService<HistoryManager>();
        // Set before the channel and keyframe view models, which read it.
        ElementEditability.Observe(element, Scene)
            .Subscribe(editable => _canEdit.Value = editable)
            .DisposeWith(_disposables);
        _canEdit.DisposeWith(_disposables);

        UseGlobalClock = animation.UseGlobalClock.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementMargin = (Element?.Start.GetObservable() ?? Observable.ReturnThenNever<MediaTime>(default))
            .CombineLatest(timelineOptions.Scale)
            .Select(t => new Thickness(t.First.TimeToPixel(t.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementWidth = (Element?.Length.GetObservable() ?? Observable.ReturnThenNever<MediaTime>(default))
            .CombineLatest(timelineOptions.Scale)
            .Select(t => t.First.TimeToPixel(t.Second))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ElementColor = (Element?.AccentColor.GetObservable() ??
                        Observable.ReturnThenNever(Media.Colors.Transparent))
            .Select(v => (IBrush)new ImmutableSolidColorBrush(v.ToAvaColor()))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        TimeOrigin = AnimationOperations.ObserveTimeOrigin(animation, animationOwner)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Margin = TimeOrigin
            .CombineLatest(timelineOptions.Scale)
            .Select(item => new Thickness(item.First.TimeToPixel(item.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SeekBarMargin = _editorClock.CurrentTime
            .CombineLatest(timelineOptions.Scale)
            .Select(item => new Thickness(item.First.TimeToPixel(item.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        (StartingBarMargin, EndingBarMargin, PanelWidth) = TimelineHelper.CreateSceneBarMargins(
            Scene, _editorClock, timelineOptions.Scale, _disposables);

        Factory = GraphEditorViewViewModelFactory.GetFactory(this);
        Views = GraphEditorViewViewModelFactory.CreateViews(this, Factory);
        foreach (GraphEditorViewViewModel item in Views)
        {
            item.VerticalRangeChanged += OnItemVerticalRangeChanged;
            item.SelectionChanged += OnClipboardSelectionChanged;
        }

        SelectedView.Value = Views.FirstOrDefault();
        SelectedView.Skip(1).Subscribe(_ => _clipboardContextVersion++).DisposeWith(_disposables);
        var selectionCount = SelectedView.Select(view => (IObservable<int>?)view?.SelectionCount ?? Observable.ReturnThenNever(0)).Switch();
        HasSelection = selectionCount.Select(count => count > 0).ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        CanEditSelection = selectionCount.CombineLatest(_canEdit, (count, editable) => count > 0 && editable)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        CanDistributeSelection = selectionCount.CombineLatest(_canEdit, (count, editable) => count >= 3 && editable)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        CanReverseSelection = selectionCount.CombineLatest(_canEdit, (count, editable) => count >= 2 && editable)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);

        CalculateMaxHeight();

        timelineOptions.Offset.Subscribe(v => ScrollOffset.Value = ScrollOffset.Value.WithX(v.X))
            .DisposeWith(_disposables);

        CopyAllKeyFramesCommand = new AsyncReactiveCommand()
            .WithSubscribe(CopyAllKeyFramesAsync)
            .DisposeWith(_disposables);

        PasteKeyFrameAtCurrentPositionCommand = new AsyncReactiveCommand(_canEdit)
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

    // The vertical position of the value 0, in pixels.
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

    // False while the clip is locked or recovery keeps its file: the editor is then read-only.
    public IReadOnlyReactiveProperty<bool> CanEdit => _canEdit;

    public ReadOnlyReactivePropertySlim<bool> CanEditSelection { get; }

    public ReadOnlyReactivePropertySlim<bool> CanDistributeSelection { get; }

    public ReadOnlyReactivePropertySlim<bool> CanReverseSelection { get; }

    public IKeyFrameAnimation Animation { get; }

    internal ReadOnlyReactivePropertySlim<MediaTime> TimeOrigin { get; }

    public GraphEditorViewViewModel[] Views { get; }

    public GraphEditorViewViewModelFactory? Factory { get; }

    public IEditorContext EditorContext { get; }

    public HistoryManager HistoryManager { get; }

    public IReadOnlyReactiveProperty<MediaTime> CurrentTime => _editorClock.CurrentTime;

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

    public void Seek(MediaTime time) => _editorClock.Seek(time);

    public void UpdatePointerPosition(double positionX)
    {
        float scale = Options.Value.Scale;
        _pointerPosition = positionX.PixelToTime(scale);
    }

    // On a recovery-protected clip only keys whose easing could not be read may go, as removing each repairs it.
    internal bool CanDeleteKeyFrames(IEnumerable<IKeyFrame> keyFrames)
        => keyFrames.All(key => ElementEditability.CanRepair(Element, key, Scene));

    internal bool DeleteKeyFrames(IEnumerable<IKeyFrame> keyFrames)
    {
        var selected = keyFrames.Where(Animation.KeyFrames.Contains).ToArray();
        if (selected.Length == 0 || !CanDeleteKeyFrames(selected)) return false;
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
            if (Element != null) ElementRecoveryService.TryCompleteRepair(Element, HistoryManager);
        }, CommandNames.RemoveKeyFrame);
        return true;
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
        if (!CanEdit.Value) return;
        var newValue = !UseGlobalClock.Value;
        _logger.LogInformation("Updating UseGlobalClock to {Value}", newValue);
        ((IKeyFrameAnimation)Animation).UseGlobalClock.Value = newValue;
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

    public MediaTime ConvertKeyTime(MediaTime globalkeyTime)
    {
        _logger.LogInformation("Converting key time {GlobalKeyTime}", globalkeyTime);
        return AnimationOperations.ConvertKeyTime(Animation, globalkeyTime, Scene, _animationOwner);
    }

    public void DropEasing(Easing easing, MediaTime keyTime)
    {
        MediaTime converted = ConvertKeyTime(keyTime);
        // Replacing an easing that could not be read repairs a recovery-protected clip, so it stays allowed there.
        if (KeyFrameClipboardCommands.FindEasingDropTarget(Animation, converted, Scene) is { } target
                ? !ElementEditability.CanRepair(Element, target, Scene)
                : !CanEdit.Value)
            return;

        _logger.LogInformation("Dropping easing at key time {KeyTime}", keyTime);
        KeyFrameClipboardCommands.DropEasing(
            Animation, Scene, HistoryManager, easing, keyTime, converted, InsertKeyFrame, _logger, Element);
    }

    public void InsertKeyFrame(Easing easing, MediaTime keyTime)
    {
        if (!CanEdit.Value) return;
        AnimationOperations.InsertKeyFrame(
            animation: Animation,
            easing: easing,
            keyTime: keyTime,
            logger: _logger,
            fallbackOwner: _animationOwner);
        HistoryManager.Commit(CommandNames.InsertKeyFrame);
    }

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
    }

    private Task CopyAllKeyFramesAsync() => KeyFrameClipboardCommands.CopyAnimationAsync(Animation, _logger);

    internal async Task PasteKeyFrameAtPositionAsync(MediaTime pointerPosition, IClipboard? clipboard = null)
    {
        clipboard ??= ClipboardHelper.GetClipboard();
        if (!IsClipboardContextActive || clipboard == null || SelectedView.Value == null) return;
        var canPaste = CapturePasteContext();

        await KeyFrameClipboardCommands.PasteAtPositionAsync(
            clipboard,
            canPaste,
            canPaste,
            json => PasteKeyFrame(json, pointerPosition),
            json => PasteSelection(json, ConvertKeyTime(pointerPosition)),
            PasteAnimation,
            _logger);
    }

    private void PasteAnimation(string json)
    {
        KeyFrameClipboardCommands.PasteAnimation(EditorContext, Animation, json, _logger);
    }

    private void PasteKeyFrame(string json, MediaTime pointerPosition)
    {
        KeyFrameClipboardCommands.PasteKeyFrame(
            EditorContext, Animation, json, _ => ConvertKeyTime(pointerPosition),
            easing => InsertKeyFrame(easing, pointerPosition), _logger);
    }
}
