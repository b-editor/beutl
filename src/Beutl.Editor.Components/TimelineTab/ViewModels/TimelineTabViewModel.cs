using System.Collections.Specialized;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Models;
using Beutl.Editor.Components.TimelineTab.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed class TimelineTabViewModel : IToolContext, IContextCommandHandler, IContextCommandStateNotifier
{
    private readonly ILogger _logger = Log.CreateLogger<TimelineTabViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly Subject<LayerHeaderViewModel> _layerHeightChanged = new();
    private readonly Subject<System.Reactive.Unit> _canExecuteChangedSubject = new();
    private readonly Dictionary<int, TrackedLayerTopObservable> _trackerCache = [];
    private bool _isDisposed;

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
        FrameSelectionRange = new FrameSelectionRange(Scale).DisposeWith(_disposables);

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

        AddElement.Subscribe(desc => editorContext.GetRequiredService<IElementAdder>().AddElement(desc)).AddTo(_disposables);

        Paste.Subscribe(PasteCore)
            .AddTo(_disposables);

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

        DeleteFrameCache = HoveredCacheBlock.Select(v => v != null)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is not { } block) return;

                _logger.LogInformation("Deleting frame cache for block starting at frame {StartFrame}.",
                    block.StartFrame);
                if (block.IsLocked)
                {
                    BufferStatus.UnlockCache(block.StartFrame, block.StartFrame + block.LengthFrame);
                }

                BufferStatus.DeleteCache(block.StartFrame, block.StartFrame + block.LengthFrame);
            });

        LockFrameCache = HoveredCacheBlock.Select(v => v?.IsLocked == false)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is not { } block) return;

                _logger.LogInformation("Locking frame cache for block starting at frame {StartFrame}.",
                    block.StartFrame);
                BufferStatus.LockCache(block.StartFrame, block.StartFrame + block.LengthFrame);
                BufferStatus.UpdateBlocks();
            });

        UnlockFrameCache = HoveredCacheBlock.Select(v => v?.IsLocked == true)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is not { } block) return;

                _logger.LogInformation("Unlocking frame cache for block starting at frame {StartFrame}.",
                    block.StartFrame);
                BufferStatus.UnlockCache(block.StartFrame, block.StartFrame + block.LengthFrame);
                BufferStatus.UpdateBlocks();
            });

        // CanExecute がレイザーモード切替に追従するよう変更通知へ流す。
        IsRazorMode
            .Subscribe(_ => RaiseCanExecuteChanged())
            .AddTo(_disposables);

        // The Undo/Redo flush hook now lives in ElementNudgeService, wired to
        // HistoryManager.BeforeMutation by the editor context.

        _logger.LogInformation("TimelineTabViewModel initialized successfully.");
    }

    private void RaiseCanExecuteChanged()
    {
        if (!_isDisposed)
        {
            _canExecuteChangedSubject.OnNext(System.Reactive.Unit.Default);
        }
    }

    private void OnSetStartTimeToPointerPosition()
    {
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetStart(Scene, ClickedFrame);
    }

    private void OnSetEndTimeToPointerPosition()
    {
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = ClickedFrame + TimeSpan.FromSeconds(1d / rate);
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetEnd(Scene, time);
    }

    private void OnSetStartTimeToCurrentTime()
    {
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetStart(Scene, CurrentTime.Value);
    }

    private void OnSetEndTimeToCurrentTime()
    {
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = CurrentTime.Value + TimeSpan.FromSeconds(1d / rate);
        EditorContext.GetRequiredService<ISceneTimeRangeService>().SetEnd(Scene, time);
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

    public ReactiveCommand<ElementDescription> AddElement { get; } = new();

    public CoreList<ElementViewModel> Elements { get; } = [];

    public CoreList<Guid> ThumbnailsDisabledElements { get; } = [];

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = [];

    public CoreList<LayerHeaderViewModel> LayerHeaders { get; } = [];

    public ReactiveCommand Paste { get; } = new();

    public ReactiveCommand Duplicate { get; } = new();

    public ReactiveCommand<(TimeRange Range, int ZIndex)> ScrollTo { get; } = new();

    public ReactiveCommandSlim SetStartTimeToPointerPosition { get; } = new();

    public ReactiveCommandSlim SetEndTimeToPointerPosition { get; } = new();

    public ReactiveCommandSlim SetStartTimeToCurrentTime { get; } = new();

    public ReactiveCommandSlim SetEndTimeToCurrentTime { get; } = new();

    public CoreList<SceneMarker> Markers => Scene.Markers;

    public ReactiveCommandSlim DeleteAllFrameCache { get; }

    public ReactiveCommandSlim DeleteFrameCache { get; }

    public ReactiveCommandSlim LockFrameCache { get; }

    public ReactiveCommandSlim UnlockFrameCache { get; }

    public ReactiveProperty<bool> AutoAdjustSceneDuration { get; }

    public ReactiveProperty<bool> IsSnapEnabled { get; }

    public ReactivePropertySlim<double?> SnapBarPosition { get; } = new();

    public ReactivePropertySlim<CacheBlock?> HoveredCacheBlock { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsLockCacheButtonEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUnlockCacheButtonEnabled { get; }

    public FrameSelectionRange FrameSelectionRange { get; }

    public TimeSpan ClickedFrame { get; set; }

    public Point ClickedPosition { get; set; }

    public HashSet<ElementViewModel> SelectedElements { get; } = [];

    public ReactivePropertySlim<bool> IsRazorMode { get; } = new();

    public ToolTabExtension Extension => TimelineTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IObservable<LayerHeaderViewModel> LayerHeightChanged => _layerHeightChanged;

    public IObservable<System.Reactive.Unit> CanExecuteChanged => _canExecuteChangedSubject;

    public string Header => Strings.Timeline;

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

    /// <summary>
    /// Returns true when a duplicate was placed and committed. Alt+drag uses the
    /// return value to decide whether to fall back to a plain move.
    /// </summary>
    internal bool DuplicateElementsAt(IReadOnlyList<Element> sourceElements, TimeSpan anchorStart, int anchorZIndex)
    {
        if (sourceElements.Count == 0)
        {
            _logger.LogWarning("DuplicateElementsAt called with empty sourceElements; investigate caller.");
            return false;
        }

        if (Scene.Uri is null)
        {
            NotificationService.ShowWarning(Strings.Duplicate_Failed, Strings.Duplicate_ProjectNotSaved);
            return false;
        }

        try
        {
            return EditorContext.GetRequiredService<IElementDuplicateService>()
                .DuplicateAtPosition(Scene, sourceElements, anchorStart, anchorZIndex);
        }
        catch (Exception ex)
        {
            HandleDuplicateException(ex);
            return false;
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

    private async void PasteCore()
    {
        try
        {
            ElementPasteOutcome outcome = await EditorContext.GetRequiredService<IElementClipboardService>()
                .PasteAsync(Scene, ClickedFrame, CalculateClickedLayer());

            if (outcome.Pasted && outcome.ScrollTo.Duration > TimeSpan.Zero)
            {
                ScrollTo.Execute((outcome.ScrollTo, outcome.ScrollToZIndex));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred.");
            NotificationService.ShowError(MessageStrings.UnexpectedError, ex.Message);
        }
    }

    // ClickedPositionから最後にクリックしたレイヤーを計算します。
    public int CalculateClickedLayer()
    {
        _logger.LogDebug("Calculating clicked layer from position {Position}.", ClickedPosition);
        return ToLayerNumber(ClickedPosition.Y);
    }

    // zindexまでLayerHeaderを追加します。
    private void AddLayerHeaders(int count)
    {
        _logger.LogDebug("Adding layer headers up to count {Count}.", count);
        if (LayerHeaders.Count != 0)
        {
            LayerHeaderViewModel last = LayerHeaders[LayerHeaders.Count - 1];

            for (int i = last.Number.Value + 1; i < count; i++)
            {
                LayerHeaders.Add(new LayerHeaderViewModel(i, this));
            }

            if (Options.Value.MaxLayerCount != LayerHeaders.Count)
            {
                Options.Value = Options.Value with { MaxLayerCount = LayerHeaders.Count };

                _logger.LogDebug("The number of layers has been changed. ({Count})", count);
            }
        }
    }

    private void TryApplyLayerCount(int count)
    {
        _logger.LogDebug("Trying to apply layer count {Count}.", count);
        if (LayerHeaders.Count > count)
        {
            for (int i = LayerHeaders.Count - 1; i >= count; i--)
            {
                LayerHeaderViewModel item = LayerHeaders[i];
                if (item.ItemsCount.Value > 0)
                    break;

                LayerHeaders.RemoveAt(i);
            }

            if (Options.Value.MaxLayerCount != LayerHeaders.Count)
            {
                Options.Value = Options.Value with { MaxLayerCount = LayerHeaders.Count };

                _logger.LogDebug("The number of layers has been changed. ({Count})", LayerHeaders.Count);
            }
        }
        else
        {
            AddLayerHeaders(count);
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        _logger.LogInformation("Reading TimelineViewModel state from JSON.");

        if (json.TryGetPropertyValue(nameof(LayerHeaders), out JsonNode? layersNode)
            && layersNode is JsonArray layersArray)
        {
            foreach ((LayerHeaderViewModel layer, JsonObject item) in layersArray.OfType<JsonObject>()
                         .Select(v =>
                             v.TryGetPropertyValueAsJsonValue(nameof(LayerHeaderViewModel.Number), out int number)
                                 ? (number, v)
                                 : (-1, null))
                         .Where(v => v.Item2 != null)
                         .Join(
                             LayerHeaders,
                             x => x.Item1,
                             y => y.Number.Value,
                             (x, y) => (y, x.Item2!)))
            {
                layer.ReadFromJson(item);
                _logger.LogDebug("LayerHeader {Number} state restored from JSON.", layer.Number.Value);
            }
        }

        if (json.TryGetPropertyValue(nameof(Inlines), out JsonNode? inlinesNode)
            && inlinesNode is JsonArray inlinesArray)
        {
            RestoreInlineAnimation(inlinesArray);
        }

        if (json.TryGetPropertyValue(nameof(ThumbnailsDisabledElements), out JsonNode? ThumbnailsDisabledNode)
            && ThumbnailsDisabledNode is JsonArray thumbnailsDisabledArray)
        {
            ThumbnailsDisabledElements.Clear();
            foreach (JsonNode? item in thumbnailsDisabledArray)
            {
                if (item is JsonValue value
                    && value.TryGetValue(out string? guidStr)
                    && Guid.TryParse(guidStr, out Guid id)
                    && Scene.Children.Any(e => e.Id == id))
                {
                    ThumbnailsDisabledElements.Add(id);
                }
            }
        }

        _logger.LogInformation("TimelineViewModel state read from JSON successfully.");
    }

    private void RestoreInlineAnimation(JsonArray inlinesArray)
    {
        _logger.LogInformation("Restoring inline animations from JSON.");

        static (Guid ElementId, Guid AnimationId) GetIds(JsonObject v)
        {
            return v.TryGetPropertyValueAsJsonValue("ElementId", out Guid elementId)
                   && v.TryGetPropertyValueAsJsonValue("AnimationId", out Guid anmId)
                ? (elementId, anmId)
                : (Guid.Empty, Guid.Empty);
        }

        foreach ((Element element, Guid anmId) in inlinesArray.OfType<JsonObject>()
                     .Select(GetIds)
                     .Where(x => x.AnimationId != Guid.Empty && x.ElementId != Guid.Empty)
                     .Join(Scene.Children,
                         x => x.ElementId,
                         y => y.Id,
                         (x, y) => (y, x.AnimationId)))
        {
            IAnimatablePropertyAdapter? anmProp = null;
            EngineObject? engineObject = null;

            void FindAndSetAncestor(Span<object> span, KeyFrameAnimation kfAnm)
            {
                for (int i = 0; i < span.Length; i++)
                {
                    switch (span[i])
                    {
                        case IAnimatablePropertyAdapter anmProp2 when ReferenceEquals(anmProp2.Animation, kfAnm):
                            anmProp = anmProp2;
                            return;
                        case EngineObject engineObject2:
                            engineObject = engineObject2;
                            return;
                    }
                }
            }

            bool Predicate(Stack<object> stack, object obj)
            {
                if (obj is IProperty { Animation: KeyFrameAnimation kfAnm } && kfAnm.Id == anmId)
                {
                    using var pooledArray = new PooledArray<object>(stack.Count);
                    // 同じものが見つかった時に、上の階層から、IAbstractPropertyやAnimatableを探す。
                    stack.CopyTo(pooledArray._array, 0);
                    FindAndSetAncestor(pooledArray.Span, kfAnm);
                    return true;
                }

                return false;
            }

            var searcher = new ObjectSearcher(element, Predicate);

            //このコードは例えばPenの中にあるアニメーションなどには対応できない。
            //var searcher = new ObjectSearcher(
            //    element,
            //    v => v is IAbstractAnimatableProperty { Animation: KeyFrameAnimation kfAnm } && kfAnm.Id == anmId);

            if (searcher.Search() is IProperty { Animation: KeyFrameAnimation anm } prop)
            {
                if (anmProp != null)
                {
                    AttachInline(anmProp, element);
                    _logger.LogDebug("Inline animation attached for element {ElementId} and animation {AnimationId}.",
                        element.Id, anmId);
                }
                else if (engineObject != null)
                {
                    try
                    {
                        Type type = typeof(AnimatablePropertyAdapter<>).MakeGenericType(anm.ValueType);
                        var createdProp =
                            (IAnimatablePropertyAdapter)Activator.CreateInstance(type, prop, engineObject)!;
                        AttachInline(createdProp, element);
                        _logger.LogDebug(
                            "Inline animation created and attached for element {ElementId} and animation {AnimationId}.",
                            element.Id, anmId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "An exception occurred while restoring the inline animation for element {ElementId} and animation {AnimationId}.",
                            element.Id, anmId);
                    }
                }
            }
        }

        _logger.LogInformation("Inline animations restored from JSON successfully.");
    }

    public void WriteToJson(JsonObject json)
    {
        _logger.LogInformation("Writing TimelineViewModel state to JSON.");

        var inlines = new JsonArray();
        foreach (InlineAnimationLayerViewModel item in Inlines.OrderBy(v => v.Index.Value))
        {
            if (item.Property.Animation is KeyFrameAnimation { Id: Guid anmId })
            {
                Guid elementId = item.Element.Model.Id;

                inlines.Add(new JsonObject { ["AnimationId"] = anmId, ["ElementId"] = elementId });
                _logger.LogDebug(
                    "Inline animation state written to JSON for element {ElementId} and animation {AnimationId}.",
                    elementId, anmId);
            }
        }

        json[nameof(Inlines)] = inlines;

        var thumbnailsDisabledArray = new JsonArray();
        foreach (Guid id in ThumbnailsDisabledElements)
        {
            thumbnailsDisabledArray.Add(id.ToString());
        }

        json[nameof(ThumbnailsDisabledElements)] = thumbnailsDisabledArray;

        _logger.LogInformation("TimelineViewModel state written to JSON successfully.");
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

    public IObservable<double> GetTrackedLayerTopObservable(IObservable<int> layer)
    {
        return layer.Select(GetTrackedLayerTopObservable).Switch();
    }

    public IObservable<double> GetTrackedLayerTopObservable(int zIndex)
    {
        lock (_trackerCache)
        {
            if (!_trackerCache.TryGetValue(zIndex, out TrackedLayerTopObservable? value))
            {
                value = new TrackedLayerTopObservable(zIndex, this);
                _trackerCache.Add(zIndex, value);
            }

            return value;
        }
    }

    public double CalculateLayerTop(int layer)
    {
        AddLayerHeaders(layer + 1);

        double sum = 0;
        for (int i = 0; i < layer; i++)
        {
            sum += LayerHeaders[i].Height.Value;
        }

        return sum;
    }

    public int ToLayerNumber(double pixel)
    {
        double sum = 0;

        for (int i = 0; i < LayerHeaders.Count; i++)
        {
            LayerHeaderViewModel cur = LayerHeaders[i];
            if (sum <= pixel && pixel <= (sum += cur.Height.Value))
            {
                return i;
            }
        }

        double delta = pixel - sum;
        int addCount = (int)Math.Ceiling(delta / FrameNumberHelper.LayerHeight);
        int zIndex = addCount + LayerHeaders.Count;
        AddLayerHeaders(zIndex + 1);

        return zIndex;
    }

    public int ToLayerNumber(Thickness thickness)
    {
        double sum = 0;

        for (int i = 0; i < LayerHeaders.Count; i++)
        {
            LayerHeaderViewModel cur = LayerHeaders[i];
            double top = thickness.Top + (FrameNumberHelper.LayerHeight / 2);
            if (sum <= top && top <= (sum += cur.Height.Value))
            {
                return i;
            }
        }

        double delta = thickness.Top - sum;
        int addCount = (int)Math.Ceiling(delta / FrameNumberHelper.LayerHeight);
        int zIndex = addCount + LayerHeaders.Count;
        AddLayerHeaders(zIndex + 1);

        return zIndex;
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

    internal void RaiseLayerHeightChanged(LayerHeaderViewModel value)
    {
        _layerHeightChanged.OnNext(value);
    }

    public object? GetService(Type serviceType)
    {
        return EditorContext.GetService(serviceType);
    }

    public ElementViewModel? GetViewModelFor(Element element)
    {
        return Elements.FirstOrDefault(x => x.Model == element);
    }

    public bool CanExecute(ContextCommandExecution execution)
    {
        return execution.CommandName switch
        {
            "Copy" or "Cut" or "Delete" or "Exclude" or "Duplicate" => SelectedElements.Count > 0,
            "NudgeLeftFrame" or "NudgeRightFrame"
                or "NudgeLeftLarge" or "NudgeRightLarge"
                or "NudgeLeftSecond" or "NudgeRightSecond" => SelectedElements.Count > 0,
            "ToggleGroup" => SelectedElements.FirstOrDefault() is { } first
                && (first.CanUngroupSelectedElements() || first.CanGroupSelectedElements()),
            "ExitRazorMode" => IsRazorMode.Value,
            "Paste" or "SetStartTime" or "SetEndTime" or "ToggleRazorMode" => true,
            // Rename / Split など Execute で対応 case が無いコマンドは false を返し、
            // パレットやショートカット経路で誤って enabled として扱われないようにする。
            _ => false,
        };
    }

    public void Execute(ContextCommandExecution execution)
    {
        _logger.LogDebug("Executing context command {CommandName}.", execution.CommandName);

        // Nudge 連打を 1 Undo にまとめる debounce 中に他コマンドが Commit すると、
        // 双方が同じ transaction に積まれて 1 Undo で一緒に取り消されてしまう。
        // Nudge 以外のコマンドを実行する直前に Nudge 分を確定させて分離する。
        if (!execution.CommandName.StartsWith("Nudge", StringComparison.Ordinal))
        {
            FlushPendingNudgeCommit();
        }

        switch (execution.CommandName)
        {
            case "Paste":
                Paste.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                    _logger.LogDebug("Paste command executed and KeyEventArgs handled.");
                }

                break;
            case "Duplicate":
                Duplicate.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "Copy":
                SelectedElements.FirstOrDefault()?.Copy.Execute();
                break;
            case "Cut":
                SelectedElements.FirstOrDefault()?.Cut.Execute();
                break;
            case "Delete":
                SelectedElements.FirstOrDefault()?.Delete.Execute();
                break;
            case "Exclude":
                SelectedElements.FirstOrDefault()?.Exclude.Execute();
                break;
            case "SetStartTime":
                SetStartTimeToCurrentTime.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "SetEndTime":
                SetEndTimeToCurrentTime.Execute();
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "ToggleGroup":
                var first = SelectedElements.FirstOrDefault();
                if (first?.CanUngroupSelectedElements() == true)
                {
                    first.UngroupSelectedElements.Execute();
                }
                else if (first?.CanGroupSelectedElements() == true)
                {
                    first.GroupSelectedElements.Execute();
                }

                break;
            case "ToggleRazorMode" when !IsTextInputFocused(execution.KeyEventArgs):
                IsRazorMode.Value = !IsRazorMode.Value;
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "ExitRazorMode" when !IsTextInputFocused(execution.KeyEventArgs):
                if (IsRazorMode.Value)
                {
                    IsRazorMode.Value = false;
                    if (execution.KeyEventArgs != null)
                    {
                        execution.KeyEventArgs.Handled = true;
                    }
                }

                break;
            case "NudgeLeftFrame" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(-1, NudgeUnit.Frame);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeRightFrame" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(+1, NudgeUnit.Frame);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeLeftLarge" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(-1, NudgeUnit.Large);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeRightLarge" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(+1, NudgeUnit.Large);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeLeftSecond" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(-1, NudgeUnit.Second);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
            case "NudgeRightSecond" when !IsTextInputFocused(execution.KeyEventArgs):
                NudgeSelectedElements(+1, NudgeUnit.Second);
                if (execution.KeyEventArgs != null)
                {
                    execution.KeyEventArgs.Handled = true;
                }

                break;
        }
    }

    // ナッジ・ToggleRazorMode 系のショートカットがテキスト入力中に発火しないようにする。
    // TextBox を直接見るだけでは AutoCompleteBox/NumericUpDown/MaskedTextBox 等の
    // ラッパー経由でフォーカスされた埋め込み TextBox を検知できないので、Source の
    // ancestor 連鎖を辿って TextBox を探す。
    private static bool IsTextInputFocused(KeyEventArgs? args)
    {
        if (args?.Source is not Visual visual) return false;
        return visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null;
    }

    private enum NudgeUnit { Frame, Large, Second }

    private void NudgeSelectedElements(int direction, NudgeUnit unit)
    {
        // Anchor on the leftmost selected element so the resulting delta lands
        // on the frame grid regardless of HashSet iteration order.
        ElementViewModel? first = SelectedElements
            .OrderBy(e => e.Model.Start)
            .ThenBy(e => e.Model.ZIndex)
            .FirstOrDefault();
        if (first is null) return;

        IReadOnlyList<ElementViewModel> targets = first.GetGroupOrSelectedElements();
        if (targets.Count == 0) return;

        int rate = Scene.FindHierarchicalParent<Project>()?.GetFrameRate() ?? 30;
        int frames = unit switch
        {
            NudgeUnit.Frame => direction,
            NudgeUnit.Large => direction * 10,
            NudgeUnit.Second => direction * rate,
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unhandled NudgeUnit."),
        };

        EditorContext.GetRequiredService<IElementNudgeService>()
            .Nudge(Scene, targets.Select(x => x.Model).ToArray(), frames);
    }

    private void FlushPendingNudgeCommit()
    {
        if (_isDisposed) return;
        try
        {
            EditorContext.GetRequiredService<IElementNudgeService>().Flush();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "Pending nudge flush dropped: editor context already disposed.");
        }
    }

    public void RazorSplitAt(TimeSpan time, bool acrossAllLayers)
    {
        IReadOnlyList<ElementViewModel> targets = acrossAllLayers
            ? Elements.Where(e => e.Model.Range.Contains(time)).ToArray()
            : Elements.Where(e => e.Model.Range.Contains(time) && e.Model.ZIndex == CalculateClickedLayer()).ToArray();

        if (targets.Count == 0)
        {
            return;
        }

        targets[0].SplitAt(targets, time);
    }

    public async Task AutoSplitBySilenceAsync(
        SilenceDetectionOptions options,
        SilenceSplitMode mode,
        CancellationToken cancellationToken = default)
    {
        if (EditorContext.GetService<IEditorSelection>()?.SelectedObject.Value is not Element element
            || FindAudioThumbnailsProvider(element) is not { } provider)
        {
            NotificationService.ShowWarning(Strings.AutoSplitBySilence, MessageStrings.AutoSplitBySilence_NoAudioElement);
            return;
        }

        TimeSpan duration = element.Range.Duration;
        if (duration <= TimeSpan.Zero)
        {
            NotificationService.ShowWarning(Strings.AutoSplitBySilence, MessageStrings.AutoSplitBySilence_NoSilenceDetected);
            return;
        }

        SilenceSplitOutcome outcome;
        try
        {
            const int SamplesPerChunk = 4096;
            int chunkCount = Math.Clamp((int)(duration.TotalSeconds * 20), 200, 8000);

            var chunks = new List<WaveformChunk>();
            await foreach (WaveformChunk chunk in provider.GetWaveformChunksAsync(chunkCount, SamplesPerChunk, ThumbnailCacheService.Instance, cancellationToken))
            {
                chunks.Add(chunk);
            }

            IReadOnlyList<SilenceRegion> localRegions = SilenceDetector.Detect(chunks, duration, chunkCount, options);
            if (localRegions.Count == 0)
            {
                NotificationService.ShowInformation(Strings.AutoSplitBySilence, MessageStrings.AutoSplitBySilence_NoSilenceDetected);
                return;
            }

            // Detector regions are element-local (0-based); the split service wants scene-timeline coordinates.
            TimeSpan offset = element.Start;
            var timelineRegions = new SilenceRegion[localRegions.Count];
            for (int i = 0; i < localRegions.Count; i++)
            {
                timelineRegions[i] = new SilenceRegion(
                    localRegions[i].Start + offset,
                    localRegions[i].End + offset);
            }

            outcome = EditorContext.GetRequiredService<ISilenceSplitService>()
                .SplitBySilence(Scene, [element], timelineRegions, mode);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto split by silence failed while analyzing the waveform.");
            NotificationService.ShowError(Strings.AutoSplitBySilence, MessageStrings.AutoSplitBySilence_AnalysisFailed);
            return;
        }

        if (outcome.SplitCount == 0 && outcome.DeletedCount == 0)
        {
            NotificationService.ShowInformation(Strings.AutoSplitBySilence, MessageStrings.AutoSplitBySilence_NoSilenceDetected);
            return;
        }

        NotificationService.ShowSuccess(
            Strings.AutoSplitBySilence,
            string.Format(MessageStrings.AutoSplitBySilence_Completed, outcome.SplitCount, outcome.DeletedCount));
    }

    private static IThumbnailsProvider? FindAudioThumbnailsProvider(Element element)
    {
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is IThumbnailsProvider { ThumbnailsKind: ThumbnailsKind.Audio } provider)
                return provider;
        }

        return null;
    }

    private sealed class TrackedLayerTopObservable(int layerNum, TimelineTabViewModel timeline)
        : LightweightObservableBase<double>, IDisposable
    {
        private IDisposable? _disposable1;
        private IDisposable? _disposable2;

        protected override void Deinitialize()
        {
            _disposable1?.Dispose();
            _disposable2?.Dispose();
            timeline._trackerCache.Remove(layerNum);
        }

        protected override void Initialize()
        {
            _disposable1 = timeline.LayerHeaders.CollectionChangedAsObservable()
                .Subscribe(OnCollectionChanged);

            _disposable2 = timeline.LayerHeightChanged.Subscribe(OnLayerHeightChanged);
        }

        private void OnLayerHeightChanged(LayerHeaderViewModel obj)
        {
            if (obj.Number.Value < layerNum)
            {
                PublishNext(timeline.CalculateLayerTop(layerNum));
            }
        }

        protected override void Subscribed(IObserver<double> observer, bool first)
        {
            observer.OnNext(timeline.CalculateLayerTop(layerNum));
        }

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs obj)
        {
            if (obj.Action == NotifyCollectionChangedAction.Move)
            {
                if (layerNum != obj.OldStartingIndex
                    && ((layerNum > obj.OldStartingIndex && layerNum <= obj.NewStartingIndex)
                        || (layerNum < obj.OldStartingIndex && layerNum >= obj.NewStartingIndex)))
                {
                    PublishNext(timeline.CalculateLayerTop(layerNum));
                }
            }
        }

        public void Dispose()
        {
            PublishCompleted();
        }
    }
}
