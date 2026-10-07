using System.Reactive.Subjects;
using Avalonia;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel : IToolContext, IContextCommandHandler, IContextCommandStateNotifier
{
    private readonly ILogger _logger = Log.CreateLogger<TimelineTabViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly Subject<LayerHeaderViewModel> _layerHeightChanged = new();
    private readonly Subject<System.Reactive.Unit> _canExecuteChangedSubject = new();
    private readonly Dictionary<int, TrackedLayerTopObservable> _trackerCache = [];
    private bool _isDisposed;
    private bool _updatingToolMode;
    private bool _addingLayerHeaders;
    private int _pendingLayerHeaderCount;

    public TimelineTabViewModel(IEditorContext editorContext)
    {
        _logger.LogInformation("Initializing TimelineTabViewModel.");
        EditorContext = editorContext;
        var timelineOptions = editorContext.GetRequiredService<ITimelineOptionsProvider>();
        var editorClock = editorContext.GetRequiredService<IEditorClock>();
        Scene = timelineOptions.Scene;
        Scale = timelineOptions.Scale;
        Options = timelineOptions.Options;
        CurrentTime = editorClock.CurrentTime;
        MaximumTime = editorClock.MaximumTime;
        BufferStatus = editorContext.GetRequiredService<IBufferStatus>();

        SeekBarMargin = CurrentTime
            .CombineLatest(Scale)
            .Select(item => new Thickness(Math.Max(item.First.TimeToPixel(item.Second), 0), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        StartingBarMargin = Scene.GetObservable(Scene.StartProperty)
            .CombineLatest(Scale)
            .Select(item => item.First.TimeToPixel(item.Second))
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        EndingBarMargin = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(Scale, StartingBarMargin)
            .Select(item => item.First.TimeToPixel(item.Second) + item.Third.Left)
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        PanelWidth = MaximumTime
            .CombineLatest(
                Scene.GetObservable(Scene.DurationProperty),
                Scene.GetObservable(Scene.StartProperty),
                CurrentTime)
            .Select(i => TimeSpan.FromTicks(
                Math.Max(
                    Math.Max(i.First.Ticks, i.Second.Ticks + i.Third.Ticks),
                    i.Fourth.Ticks)))
            .CombineLatest(Scale)
            .Select(i => i.First.TimeToPixel(i.Second) + 500)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        AddElement.WithSubscribe(AddElementCore).AddTo(_disposables);

        Paste = new AsyncReactiveCommand()
            .WithSubscribe(PasteCore)
            .DisposeWith(_disposables);

        Duplicate.Subscribe(DuplicateSelectedElements)
            .AddTo(_disposables);

        TimelineOptions options = Options.Value;
        LayerHeaders.AddRange(Enumerable.Range(0, options.MaxLayerCount)
            .Select(num => new LayerHeaderViewModel(num, this)));
        if (Scene.Children.Count > 0)
        {
            AddLayerHeaders(Scene.Children.Max(i => i.ZIndex) + 1);
            Elements.EnsureCapacity(Scene.Children.Count);
            Elements.AddRange(Scene.Children.Select(item => new ElementViewModel(item, this)));
        }

        // A persisted layer-only model (solo/mute/lock on a clipless row) still
        // feeds the compositor, so it needs a header to show and clear its flags.
        if (Scene.Layers.Count > 0)
        {
            AddLayerHeaders(Scene.Layers.Max(l => l.ZIndex) + 1);
        }

        Scene.Children.TrackCollectionChanged(
                (idx, item) =>
                {
                    _logger.LogDebug("Element added {Id}.", item.Id);
                    AddLayerHeaders(item.ZIndex + 1);
                    Elements.Insert(idx, new ElementViewModel(item, this));
                },
                (idx, _) =>
                {
                    ElementViewModel element = Elements[idx];
                    _logger.LogDebug("Element removed {Id}.", element.Model.Id);
                    SelectedElements.Remove(element);
                    Elements.RemoveAt(idx);
                    element.Dispose();
                },
                () =>
                {
                    _logger.LogDebug("All elements cleared.");
                    ElementViewModel[] tmp = [.. Elements];
                    Elements.Clear();
                    SelectedElements.Clear();
                    foreach (ElementViewModel? item in tmp)
                    {
                        item.Dispose();
                    }
                })
            .AddTo(_disposables);

        Options.Select(x => x.MaxLayerCount)
            .DistinctUntilChanged()
            .Subscribe(TryApplyLayerCount);

        SetStartTimeToPointerPosition.Subscribe(OnSetStartTimeToPointerPosition);
        SetEndTimeToPointerPosition.Subscribe(OnSetEndTimeToPointerPosition);
        SetStartTimeToCurrentTime.Subscribe(OnSetStartTimeToCurrentTime);
        SetEndTimeToCurrentTime.Subscribe(OnSetEndTimeToCurrentTime);
        CloseGap = new ReactiveCommandSlim().WithSubscribe(CloseSelectedGap).DisposeWith(_disposables);
        CloseGapAtPointerPosition = new ReactiveCommandSlim().WithSubscribe(CloseGapAtPointer).DisposeWith(_disposables);
        CloseAllGaps = new ReactiveCommandSlim().WithSubscribe(CloseAllSceneGaps).DisposeWith(_disposables);
        GoToNextGap = new ReactiveCommandSlim().WithSubscribe(() => GoToGap(forward: true)).DisposeWith(_disposables);
        GoToPreviousGap = new ReactiveCommandSlim().WithSubscribe(() => GoToGap(forward: false)).DisposeWith(_disposables);
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;

        AutoAdjustSceneDuration = editorConfig.GetObservable(EditorConfig.AutoAdjustSceneDurationProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        AutoAdjustSceneDuration.Subscribe(b =>
        {
            _logger.LogDebug("AutoAdjustSceneDuration changed to {Value}.", b);
            editorConfig.AutoAdjustSceneDuration = b;
        });

        IsSnapEnabled = editorConfig.GetObservable(EditorConfig.IsTimelineSnapEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsSnapEnabled.Subscribe(b => editorConfig.IsTimelineSnapEnabled = b)
            .DisposeWith(_disposables);

        IsRippleEnabled = editorConfig.GetObservable(EditorConfig.IsRippleEnabledProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsRippleEnabled.Subscribe(b => editorConfig.IsRippleEnabled = b)
            .DisposeWith(_disposables);

        IsLockCacheButtonEnabled = HoveredCacheBlock.Select(v => v is { IsLocked: false })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsUnlockCacheButtonEnabled = HoveredCacheBlock.Select(v => v is { IsLocked: true })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        DeleteAllFrameCache = new ReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                _logger.LogInformation("Deleting all frame cache.");
                BufferStatus.ClearCache();
            });

        DeleteFrameCache = CreateCacheBlockCommand(v => v != null, block =>
        {
            _logger.LogInformation("Deleting frame cache for block starting at frame {StartFrame}.",
                block.StartFrame);
            if (block.IsLocked)
            {
                BufferStatus.UnlockCache(block.StartFrame, block.StartFrame + block.LengthFrame);
            }

            BufferStatus.DeleteCache(block.StartFrame, block.StartFrame + block.LengthFrame);
        });

        LockFrameCache = CreateCacheBlockCommand(v => v?.IsLocked == false, block =>
        {
            _logger.LogInformation("Locking frame cache for block starting at frame {StartFrame}.",
                block.StartFrame);
            BufferStatus.LockCache(block.StartFrame, block.StartFrame + block.LengthFrame);
            BufferStatus.UpdateBlocks();
        });

        UnlockFrameCache = CreateCacheBlockCommand(v => v?.IsLocked == true, block =>
        {
            _logger.LogInformation("Unlocking frame cache for block starting at frame {StartFrame}.",
                block.StartFrame);
            BufferStatus.UnlockCache(block.StartFrame, block.StartFrame + block.LengthFrame);
            BufferStatus.UpdateBlocks();
        });

        SubscribeToolMode(IsRazorMode);
        SubscribeToolMode(IsSlipMode);
        SubscribeToolMode(IsRollMode);
        SubscribeToolMode(IsSlideMode);

        // The Undo/Redo flush hook now lives in ElementNudgeService, wired to
        // HistoryManager.BeforeMutation by the editor context.

        _logger.LogInformation("TimelineTabViewModel initialized successfully.");
    }

    private async Task AddElementCore(ElementDescription description)
        => await AddElementWithResultAsync(description);

    public async Task<ElementAddResult> AddElementWithResultAsync(ElementDescription description)
    {
        ElementAddResult result = await EditorContext
            .GetRequiredService<IElementAdder>()
            .AddAsync([description], CancellationToken.None);
        if (result.IsSuccess)
        {
            Element scrollTarget = result.Items[^1].PrimaryElement;
            ScrollTo.Execute((scrollTarget.Range, scrollTarget.ZIndex));
            return result;
        }

        if (result.Failure is LockedElementLayerFailure)
        {
            NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
            return result;
        }

        _logger.LogError(
            result.Failure?.Exception,
            "Failed to add a timeline element: {FailureId}",
            result.Failure?.Id);
        NotificationService.ShowError(Strings.AddElement, MessageStrings.UnexpectedError);
        return result;
    }

    // The command acts on the cache block under the pointer when it runs, if there still is one.
    private ReactiveCommandSlim CreateCacheBlockCommand(Func<CacheBlock?, bool> canExecute, Action<CacheBlock> execute)
    {
        return HoveredCacheBlock.Select(canExecute)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is not { } block) return;

                execute(block);
            });
    }

    private void RaiseCanExecuteChanged()
    {
        if (!_isDisposed)
        {
            _canExecuteChangedSubject.OnNext(System.Reactive.Unit.Default);
        }
    }

    public Scene Scene { get; }

    public IObservable<float> Scale { get; }

    public IReactiveProperty<TimelineOptions> Options { get; }

    public IReactiveProperty<TimeSpan> CurrentTime { get; }

    public IReadOnlyReactiveProperty<TimeSpan> MaximumTime { get; }

    public IBufferStatus BufferStatus { get; }

    public IEditorContext EditorContext { get; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> StartingBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public AsyncReactiveCommand<ElementDescription> AddElement { get; } = new();

    public CoreList<ElementViewModel> Elements { get; } = [];

    public CoreList<Guid> ThumbnailsDisabledElements { get; } = [];

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = [];

    public CoreList<LayerHeaderViewModel> LayerHeaders { get; } = [];

    public AsyncReactiveCommand Paste { get; }

    public ReactiveCommand Duplicate { get; } = new();

    public ReactiveCommand<(TimeRange Range, int ZIndex)> ScrollTo { get; } = new();

    public ReactiveCommandSlim SetStartTimeToPointerPosition { get; } = new();

    public ReactiveCommandSlim SetEndTimeToPointerPosition { get; } = new();

    public ReactiveCommandSlim SetStartTimeToCurrentTime { get; } = new();

    public ReactiveCommandSlim SetEndTimeToCurrentTime { get; } = new();

    public ReactiveCommandSlim CloseGap { get; }

    public ReactiveCommandSlim CloseGapAtPointerPosition { get; }

    public ReactiveCommandSlim CloseAllGaps { get; }

    public ReactiveCommandSlim GoToNextGap { get; }

    public ReactiveCommandSlim GoToPreviousGap { get; }

    public CoreList<SceneMarker> Markers => Scene.Markers;

    public ReactiveCommandSlim DeleteAllFrameCache { get; }

    public ReactiveCommandSlim DeleteFrameCache { get; }

    public ReactiveCommandSlim LockFrameCache { get; }

    public ReactiveCommandSlim UnlockFrameCache { get; }

    public ReactiveProperty<bool> AutoAdjustSceneDuration { get; }

    public ReactiveProperty<bool> IsSnapEnabled { get; }

    public ReactiveProperty<bool> IsRippleEnabled { get; }

    public ReactivePropertySlim<double?> SnapBarPosition { get; } = new();

    public ReactivePropertySlim<CacheBlock?> HoveredCacheBlock { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsLockCacheButtonEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUnlockCacheButtonEnabled { get; }

    public TimeSpan ClickedFrame { get; set; }

    public Point ClickedPosition { get; set; }

    public HashSet<ElementViewModel> SelectedElements { get; } = [];

    public ReactivePropertySlim<bool> IsRazorMode { get; } = new();

    // Mutually exclusive with IsRazorMode and each other; SubscribeToolMode/EnforceSingleToolMode
    // clears the other flags whenever one becomes true, however it was set.
    public ReactivePropertySlim<bool> IsSlipMode { get; } = new();
    public ReactivePropertySlim<bool> IsRollMode { get; } = new();
    public ReactivePropertySlim<bool> IsSlideMode { get; } = new();

    public ToolTabExtension Extension => TimelineTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IObservable<LayerHeaderViewModel> LayerHeightChanged => _layerHeightChanged;

    public IObservable<System.Reactive.Unit> CanExecuteChanged => _canExecuteChangedSubject;

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.Timeline);

    public void Dispose()
    {
        _logger.LogInformation("Disposing TimelineViewModel.");
        // Dispose は throw しない契約。HistoryManager が先に Dispose されている等で
        // Commit が失敗しても残りのクリーンアップは必ず進める。
        try
        {
            FlushPendingNudgeCommit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush pending nudge during dispose.");
        }
        // 以降の OnNext を抑止してから内部 Subject を Dispose する。
        _isDisposed = true;
        _disposables.Dispose();
        foreach (ElementViewModel? item in Elements.GetMarshal().Value)
        {
            item.Dispose();
        }

        foreach (LayerHeaderViewModel item in LayerHeaders)
        {
            item.Dispose();
        }

        foreach (InlineAnimationLayerViewModel item in Inlines)
        {
            item.Dispose();
        }

        if (_trackerCache.Values.Count > 0)
        {
            // ToArrayの理由は
            // TrackedLayerTopObservable.DisposeでDeinitializeが呼び出され、_trackerCacheが変更されるので
            foreach (TrackedLayerTopObservable? item in _trackerCache.Values.ToArray())
            {
                item.Dispose();
            }
        }

        _layerHeightChanged.Dispose();
        _canExecuteChangedSubject.Dispose();

        Inlines.Clear();
        LayerHeaders.Clear();
        Elements.Clear();
        _logger.LogInformation("TimelineViewModel disposed successfully.");
    }

    private void DuplicateSelectedElements()
    {
        if (SelectedElements.Count == 0) return;

        if (Scene.Uri is null)
        {
            NotificationService.ShowWarning(Strings.Duplicate_Failed, Strings.Duplicate_ProjectNotSaved);
            return;
        }

        HashSet<Guid> ids = DuplicateHelper.ExpandWithGroupSiblings(
            SelectedElements.Select(s => s.Model.Id),
            Scene.Groups);

        var sources = Elements
            .Where(x => ids.Contains(x.Model.Id))
            .Select(x => x.Model)
            .ToArray();
        if (sources.Length == 0)
        {
            _logger.LogWarning(
                "Duplicate skipped: selected element IDs did not resolve to Elements. Ids={Ids}",
                string.Join(", ", ids));
            return;
        }

        try
        {
            DuplicateOutcome outcome = EditorContext.GetRequiredService<IElementDuplicateService>()
                .DuplicateAtClickedPosition(Scene, sources, ClickedFrame, CalculateClickedLayer());
            if (outcome.Success)
            {
                ScrollTo.Execute((outcome.ScrollToRange, outcome.ScrollToZIndex));
            }
            else
            {
                NotificationService.ShowError(Strings.Duplicate_Failed, string.Empty);
            }
        }
        catch (Exception ex)
        {
            HandleDuplicateException(ex);
        }
    }

    private void HandleDuplicateException(Exception ex)
    {
        switch (ex)
        {
            case IOException:
            case UnauthorizedAccessException:
                _logger.LogError(ex, "Duplicate failed: I/O error.");
                NotificationService.ShowError(Strings.Duplicate_Failed, Strings.Duplicate_IOFailed);
                break;
            default:
                _logger.LogError(ex, "An exception has occurred while duplicating.");
                NotificationService.ShowError(MessageStrings.UnexpectedError, ex.Message);
                break;
        }
    }

    private async Task PasteCore()
    {
        try
        {
            ElementPasteOutcome outcome = await EditorContext.GetRequiredService<IElementClipboardService>()
                .PasteAsync(Scene, ClickedFrame, CalculateClickedLayer());

            if (outcome.Pasted && outcome.ScrollTo.Duration > TimeSpan.Zero)
            {
                ScrollTo.Execute((outcome.ScrollTo, outcome.ScrollToZIndex));
            }
            else if (outcome.AddFailure is LockedElementLayerFailure)
            {
                NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked);
            }
            else if (outcome.AddFailure is not null)
            {
                NotificationService.ShowError(Strings.AddElement, MessageStrings.UnexpectedError);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred.");
            NotificationService.ShowError(MessageStrings.UnexpectedError, ex.Message);
        }
    }

    public void AttachInline(IAnimatablePropertyAdapter property, Element element)
    {
        _logger.LogInformation("Attaching inline animation for element {ElementId} and property {Property}.",
            element.Id, property);
        if (Inlines.Any(x => x.Element.Model == element && x.Property == property))
        {
            _logger.LogWarning("Inline animation already attached for element {ElementId}.", element.Id);
            return;
        }

        if (GetViewModelFor(element) is not { } viewModel)
        {
            _logger.LogError("Failed to attach inline animation for element {ElementId}.", element.Id);
            return;
        }

        // タイムラインのタブを開く
        Type type = typeof(InlineAnimationLayerViewModel<>).MakeGenericType(property.PropertyType);
        if (Activator.CreateInstance(type, property, this, viewModel) is InlineAnimationLayerViewModel
            anmTimelineViewModel)
        {
            Inlines.Add(anmTimelineViewModel);
            _logger.LogInformation("Inline animation attached successfully for element {ElementId}.", element.Id);
        }
        else
        {
            _logger.LogError("Failed to attach inline animation for element {ElementId}.", element.Id);
        }
    }

    public void DetachInline(InlineAnimationLayerViewModel item)
    {
        _logger.LogInformation("Detaching inline animation for element {ElementId}.", item.Element.Model.Id);
        if (item.LayerHeader.Value is { } layerHeader)
        {
            layerHeader.Inlines.Remove(item);
        }

        Inlines.Remove(item);
        item.Dispose();
        _logger.LogInformation("Inline animation detached successfully for element {ElementId}.",
            item.Element.Model.Id);
    }

    public void ClearSelected()
    {
        foreach (ElementViewModel item in SelectedElements)
        {
            item.IsSelected.Value = false;
        }

        SelectedElements.Clear();
        RaiseCanExecuteChanged();
    }

    public void SelectElement(ElementViewModel item)
    {
        SelectedElements.Add(item);
        item.IsSelected.Value = true;
        RaiseCanExecuteChanged();
    }

    public void SwitchSelectedElement(ElementViewModel item)
    {
        item.IsSelected.Value = !item.IsSelected.Value;
        if (item.IsSelected.Value)
        {
            SelectedElements.Add(item);
        }
        else
        {
            SelectedElements.Remove(item);
        }

        RaiseCanExecuteChanged();
    }

    public object? GetService(Type serviceType)
    {
        return EditorContext.GetService(serviceType);
    }

    public ElementViewModel? GetViewModelFor(Element element)
    {
        return Elements.FirstOrDefault(x => x.Model == element);
    }
}
