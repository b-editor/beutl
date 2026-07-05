using System.Collections.Immutable;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Beutl.Animation;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.ProxiesTab;
using Beutl.Editor.Components.TimelineTab.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;
using FluentAvalonia.UI.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed class ElementViewModel : IDisposable, IContextCommandHandler
{
    private readonly ILogger _logger = Log.CreateLogger<ElementViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private ImmutableHashSet<Guid>? _elementGroup;
    private readonly Subject<Unit> _thumbnailsInvalidatedSubject = new();
    private CancellationTokenSource? _thumbnailsCts;
    private IThumbnailsProvider? _currentThumbnailsProvider;
    private readonly IThumbnailCacheService _thumbnailCacheService = ThumbnailCacheService.Instance;
    private EventHandler? _thumbnailsInvalidatedHandler;
    private string? _lastThumbnailsCacheKey;
    private int _lastVisibleStart = -1;
    private int _lastVisibleEnd = -1;
    private CancellationTokenSource? _scrollThumbnailsCts;
    private readonly Subject<(int Start, int End)> _visibleRangeSubject = new();
    private readonly IProxyStore? _proxyStore;
    private readonly IProxyJobQueue? _proxyJobQueue;
    private Uri? _proxySourceUri;
    private ProxyFingerprint? _proxyFingerprint;

    public Func<int, int, List<int>>? GetMissingThumbnailIndices;

    public ElementViewModel(Element element, TimelineTabViewModel timeline)
    {
        Model = element;
        Timeline = timeline;
        Scene = timeline.Scene;

        InitializeElementGroup();

        // プロパティを構成
        IsEnabled = element.GetObservable(Element.IsEnabledProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsLocked = element.GetObservable(Element.IsLockedProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Name = element.GetObservable(CoreObject.NameProperty)
            .ToReactiveProperty()
            .AddTo(_disposables)!;
        // Skip(1) drops the initial sync emit (IsEditable is assigned later in this ctor). While
        // locked, a rename must not persist and the display must not diverge from the persisted
        // name, so snap Name.Value back to Model.Name instead of writing it.
        Name.Skip(1)
            .Subscribe(v =>
            {
                if (IsEditable.Value) Model.Name = v;
                else if (v != Model.Name) Name.Value = Model.Name;
            })
            .AddTo(_disposables);

        IObservable<int> zIndexSubject = element.GetObservable(Element.ZIndexProperty);
        Margin = Timeline.GetTrackedLayerTopObservable(zIndexSubject)
            .Select(item => new Thickness(0, item, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        BorderMargin = element.GetObservable(Element.StartProperty)
            .CombineLatest(timeline.Scale)
            .Select(item => new Thickness(item.First.TimeToPixel(item.Second), 0, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width = element.GetObservable(Element.LengthProperty)
            .CombineLatest(timeline.Scale)
            .Select(item => item.First.TimeToPixel(item.Second))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Color = element.GetObservable(Element.AccentColorProperty)
            .Select(c => c.ToAvaColor())
            .ToReactiveProperty()
            .AddTo(_disposables);

        RestBorderColor = Color.Select(v => (Avalonia.Media.Color)((Color2)v).LightenPercent(-0.15f))
            .ToReadOnlyReactivePropertySlim();

        TextColor = Color.Select(ColorGenerator.GetTextColor)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        // コマンドを構成
        Split.Where(_ => GetClickedTime != null)
            .Subscribe(_ => OnSplit(GetClickedTime!()))
            .AddTo(_disposables);

        SplitByCurrentFrame
            .Subscribe(_ => OnSplit(timeline.CurrentTime.Value))
            .AddTo(_disposables);

        Cut.Subscribe(OnCut)
            .AddTo(_disposables);

        Copy.Subscribe(async () => await OnCopy())
            .AddTo(_disposables);

        Exclude.Subscribe(OnExclude)
            .AddTo(_disposables);

        Delete.Subscribe(OnDelete)
            .AddTo(_disposables);

        Color.Skip(1)
            .Subscribe(c => Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
                .SetAccentColor(Model, c.ToBtlColor()))
            .AddTo(_disposables);

        FinishEditingAnimation.Subscribe(OnFinishEditingAnimation)
            .AddTo(_disposables);

        BringAnimationToTop.Subscribe(OnBringAnimationToTop)
            .AddTo(_disposables);

        ChangeToOriginalDuration.Subscribe(OnChangeToOriginalDuration)
            .AddTo(_disposables);

        // ZIndexが変更されたら、LayerHeaderのカウントを増減して、新しいLayerHeaderを設定する。
        zIndexSubject.Subscribe(number =>
            {
                LayerHeaderViewModel? newLH = Timeline.LayerHeaders.FirstOrDefault(i => i.Number.Value == number);

                LayerHeader.Value?.ElementRemoved(this);

                newLH?.ElementAdded(this);
                LayerHeader.Value = newLH;
            })
            .AddTo(_disposables);

        // Editable unless the element or its TimelineLayer is locked.
        IsEditable = IsLocked
            .CombineLatest(
                LayerHeader.Select(h => h is null
                    ? Observable.Return(false)
                    : h.IsLocked).Switch(),
                (el, layer) => !el && !layer)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Scope = new ElementScopeViewModel(Model, this);

        // プレビュー関連の初期化
        IsThumbnailsKindAudio = ThumbnailsKind.Select(k => k == Engine.ThumbnailsKind.Audio)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        IsThumbnailsKindVideo = ThumbnailsKind.Select(k => k == Engine.ThumbnailsKind.Video)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        InitializeThumbnails();

        _proxyStore = Timeline.EditorContext.GetService<IProxyStore>();
        _proxyJobQueue = Timeline.EditorContext.GetService<IProxyJobQueue>();
        ProxyIndicatorBrush = ProxyIndicatorState
            .Select(GetProxyStateBrush)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        ProxyIndicatorTooltip = ProxyIndicatorState
            .Select(GetProxyStateText)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        InitializeProxyIndicator();
    }

    private void InitializeProxyIndicator()
    {
        if (_proxyStore != null)
        {
            EventHandler<ProxyStoreChangedEventArgs> storeHandler = (_, _) => OnProxyStateInvalidated();
            _proxyStore.Changed += storeHandler;
            Disposable.Create(() => _proxyStore.Changed -= storeHandler).AddTo(_disposables);
        }

        if (_proxyJobQueue != null)
        {
            EventHandler<ProxyJobChangedEventArgs> jobHandler = (_, _) => OnProxyStateInvalidated();
            _proxyJobQueue.JobChanged += jobHandler;
            Disposable.Create(() => _proxyJobQueue.JobChanged -= jobHandler).AddTo(_disposables);
        }

        // Source edits raise ThumbnailsInvalidated; re-resolve the badge when the backing file changes.
        // An in-place overwrite keeps the same URI, so the fingerprint cache must be busted to re-stat.
        _thumbnailsInvalidatedSubject
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => RefreshProxyState(invalidateFingerprintCache: true))
            .AddTo(_disposables);

        RefreshProxyState();
    }

    private void InitializeThumbnails()
    {
        // プレビュー無効化の初期値を設定
        IsThumbnailsDisabled.Value = Timeline.ThumbnailsDisabledElements.Contains(Model.Id);

        // ThumbnailsDisabledElementsの変更を購読
        Timeline.ThumbnailsDisabledElements.Attached += OnThumbnailsDisabledElementsAttached;
        Timeline.ThumbnailsDisabledElements.Detached += OnThumbnailsDisabledElementsDetached;

        // IsThumbnailsDisabledが変更されたらThumbnailsDisabledElementsを更新し、プレビューを再読み込み
        IsThumbnailsDisabled.Skip(1)
            .Subscribe(isDisabled =>
            {
                if (isDisabled)
                {
                    if (!Timeline.ThumbnailsDisabledElements.Contains(Model.Id))
                    {
                        Timeline.ThumbnailsDisabledElements.Add(Model.Id);
                    }
                }
                else
                {
                    Timeline.ThumbnailsDisabledElements.Remove(Model.Id);
                }

                UpdateThumbnailsAsync();
            })
            .AddTo(_disposables);

        // Width変更とThumbnailsInvalidatedイベントをマージして、いずれかが発生してから500ms後に更新
        Observable.Merge(
                Width.Select(_ => Unit.Default),
                _thumbnailsInvalidatedSubject.AsObservable())
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdateThumbnailsAsync())
            .AddTo(_disposables);

        // スクロール時の可視範囲変更をスロットルして追加生成
        _visibleRangeSubject
            .Where(_ => !IsThumbnailsDisabled.Value)
            .Throttle(TimeSpan.FromMilliseconds(50))
            .ObserveOnUIDispatcher()
            .Subscribe(range => _ = UpdateVisibleThumbnailsAsync(range.Start, range.End))
            .AddTo(_disposables);

        // Switching PreviewSourceMode changes the decode path (proxy vs original), so an already
        // rendered filmstrip is stale until an unrelated invalidation; drop its cache and re-render.
        Scene.GetObservable(Scene.PreviewSourceModeProperty)
            .Skip(1)
            .DistinctUntilChanged()
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                if (_lastThumbnailsCacheKey != null)
                    _thumbnailCacheService.Invalidate(_lastThumbnailsCacheKey);

                UpdateThumbnailsAsync();
            })
            .AddTo(_disposables);
    }

    private void InitializeElementGroup()
    {
        _elementGroup = Scene.Groups.FirstOrDefault(x => x.Contains(Model.Id));

        Scene.Groups.Attached += OnSetAttached;
        Scene.Groups.Detached += OnSetDetached;
        _disposables.Add(Disposable.Create(() =>
        {
            Scene.Groups.Attached -= OnSetAttached;
            Scene.Groups.Detached -= OnSetDetached;
        }));

        GroupSelectedElements.Subscribe(_ => OnGroupSelectedElements())
            .AddTo(_disposables);

        UngroupSelectedElements.Subscribe(_ => OnUngroupSelectedElements())
            .AddTo(_disposables);
    }

    ~ElementViewModel()
    {
        _disposables.Dispose();
    }

    public Func<(Thickness Margin, Thickness BorderMargin, double Width), CancellationToken, Task> AnimationRequested
    {
        get;
        set;
    } = (_, _) => Task.CompletedTask;

    public Action RenameRequested { get; set; } = () => { };

    public Func<TimeSpan>? GetClickedTime { get; set; }

    public TimelineTabViewModel Timeline { get; }

    public Element Model { get; }

    public ElementScopeViewModel Scope { get; }

    public Scene Scene { get; }

    public ReadOnlyReactivePropertySlim<bool> IsEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLocked { get; }

    public ReadOnlyReactivePropertySlim<bool> IsEditable { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> BorderMargin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveProperty<bool> IsSelected { get; } = new(false);

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader { get; set; } = new();

    public ReactiveProperty<Avalonia.Media.Color> Color { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> RestBorderColor { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> TextColor { get; }

    public ReactiveCommand Split { get; } = new();

    public ReactiveCommand SplitByCurrentFrame { get; } = new();

    public AsyncReactiveCommand Cut { get; } = new();

    public ReactiveCommand Copy { get; } = new();

    public ReactiveCommand Exclude { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand GroupSelectedElements { get; } = new();

    public ReactiveCommand UngroupSelectedElements { get; } = new();

    public ReactiveCommand FinishEditingAnimation { get; } = new();

    public ReactiveCommand BringAnimationToTop { get; } = new();

    public ReactiveCommand ChangeToOriginalDuration { get; } = new();

    public ReactivePropertySlim<ThumbnailsKind> ThumbnailsKind { get; } = new(Engine.ThumbnailsKind.None);

    public ReadOnlyReactivePropertySlim<bool> IsThumbnailsKindVideo { get; }

    public ReadOnlyReactivePropertySlim<bool> IsThumbnailsKindAudio { get; }

    public ReactivePropertySlim<bool> IsThumbnailsDisabled { get; } = new();

    public ReactivePropertySlim<int> VideoThumbnailCount { get; } = new();

    public ReactivePropertySlim<int> WaveformChunkCount { get; } = new();

    public ReactivePropertySlim<bool> ShowProxyIndicator { get; } = new();

    public ReactivePropertySlim<ProxyState> ProxyIndicatorState { get; } = new(ProxyState.None);

    public ReadOnlyReactivePropertySlim<IBrush> ProxyIndicatorBrush { get; }

    public ReadOnlyReactivePropertySlim<string> ProxyIndicatorTooltip { get; }

    public event Action<int, WriteableBitmap?>? ThumbnailReady;

    public event Action? ThumbnailsClear;

    public event Action<WaveformChunk>? WaveformChunkReady;

    public event Action? WaveformClear;

    public IReadOnlyList<ElementViewModel> GetGroupOrSelectedElements()
    {
        var ids = new HashSet<Guid>();

        if (_elementGroup is { } group)
        {
            ids.UnionWith(group);
        }

        foreach (ElementViewModel item in Timeline.SelectedElements)
        {
            ids.Add(item.Model.Id);
        }

        if (ids.Count == 0)
        {
            return [];
        }

        return Timeline.Elements
            .Where(x => ids.Contains(x.Model.Id))
            .ToArray();
    }

    public bool CanGroupSelectedElements()
    {
        IReadOnlyCollection<Guid> ids = GetEditableSelectedIdsOrSelf();
        return ids.Count >= 2 && !Scene.Groups.Any(x => x.SetEquals(ids));
    }

    public bool CanUngroupSelectedElements()
    {
        IReadOnlyCollection<Guid> ids = GetEditableSelectedIdsOrSelf();
        return Scene.Groups.Any(x => x.Overlaps(ids));
    }

    public void Dispose()
    {
        // ThumbnailsInvalidatedイベントの購読を解除
        if (_currentThumbnailsProvider != null && _thumbnailsInvalidatedHandler != null)
        {
            _currentThumbnailsProvider.ThumbnailsInvalidated -= _thumbnailsInvalidatedHandler;
        }
        _thumbnailsInvalidatedSubject.Dispose();
        _visibleRangeSubject.Dispose();

        CancelThumbnailsLoading();

        _scrollThumbnailsCts?.Cancel();
        _scrollThumbnailsCts?.Dispose();
        _scrollThumbnailsCts = null;
        if (_lastThumbnailsCacheKey != null)
            _thumbnailCacheService.Invalidate(_lastThumbnailsCacheKey);

        // ThumbnailsDisabledElementsイベントの購読を解除
        Timeline.ThumbnailsDisabledElements.Attached -= OnThumbnailsDisabledElementsAttached;
        Timeline.ThumbnailsDisabledElements.Detached -= OnThumbnailsDisabledElementsDetached;

        _disposables.Dispose();
        LayerHeader.Dispose();
        Scope.Dispose();

        ThumbnailsKind.Dispose();
        VideoThumbnailCount.Dispose();
        WaveformChunkCount.Dispose();
        ShowProxyIndicator.Dispose();
        ProxyIndicatorState.Dispose();

        LayerHeader.Value = null!;
        AnimationRequested = (_, _) => Task.CompletedTask;
        GetClickedTime = null;
        GetMissingThumbnailIndices = null;
        GC.SuppressFinalize(this);
    }

    public async void AnimationRequest(int layerNum, bool affectModel = true,
        CancellationToken cancellationToken = default)
    {
        var inlines = Timeline.Inlines
            .Where(x => x.Element == this)
            .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
            .ToArray();
        var scope = Scope.PrepareAnimation();

        Thickness newMargin = new(0, Timeline.CalculateLayerTop(layerNum), 0, 0);
        Thickness oldMargin = Margin.Value;
        if (affectModel)
            Model.ZIndex = layerNum;

        Margin.Value = oldMargin;

        foreach (var (item, context) in inlines)
            item.AnimationRequest(context, newMargin, BorderMargin.Value, cancellationToken);

        Task task1 = Scope.AnimationRequest(scope, cancellationToken);
        Task task2 = AnimationRequested((newMargin, BorderMargin.Value, Width.Value), cancellationToken);

        await Task.WhenAll(task1, task2);
        Margin.Value = newMargin;
    }

    public async Task AnimationRequest(PrepareAnimationContext context, CancellationToken cancellationToken = default)
    {
        var margin = new Thickness(0, Timeline.CalculateLayerTop(Model.ZIndex), 0, 0);
        var borderMargin = new Thickness(Model.Start.TimeToPixel(Timeline.Options.Value.Scale), 0, 0, 0);
        double width = Model.Length.TimeToPixel(Timeline.Options.Value.Scale);

        BorderMargin.Value = context.BorderMargin;
        Margin.Value = context.Margin;
        Width.Value = context.Width;

        foreach (var (item, inlineContext) in context.Inlines)
        {
            item.AnimationRequest(inlineContext, margin, borderMargin, cancellationToken);
        }

        Task task1 = Scope.AnimationRequest(context.Scope, cancellationToken);
        Task task2 = AnimationRequested((margin, borderMargin, width), cancellationToken);

        await Task.WhenAll(task1, task2);
        BorderMargin.Value = borderMargin;
        Margin.Value = margin;
        Width.Value = width;
    }

    // Ripple shifts the dragged edge's delta onto neighbours, so the untouched edge must carry its
    // exact model coordinate — re-deriving it through a lossy pixel->frame round-trip would leak a
    // sub-frame delta onto the wrong side for an off-frame clip and ripple the wrong neighbours.
    internal static (TimeSpan Start, TimeSpan Length) ResolveRippleResizeBounds(
        bool leftEdge, TimeSpan roundedStart, TimeSpan roundedLength, TimeSpan modelStart, TimeSpan modelEnd)
    {
        return leftEdge
            ? (roundedStart, modelEnd - roundedStart)
            : (modelStart, roundedLength);
    }

    public async Task SubmitViewModelChanges(bool ripple = false, bool leftEdge = false)
    {
        PrepareAnimationContext context = PrepareAnimation();

        float scale = Timeline.Options.Value.Scale;
        int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan roundedStart = BorderMargin.Value.Left.PixelToTimeSpan(scale).RoundToRate(rate);
        TimeSpan roundedLength = Width.Value.PixelToTimeSpan(scale).RoundToRate(rate);
        (TimeSpan start, TimeSpan length) = ripple
            ? ResolveRippleResizeBounds(leftEdge, roundedStart, roundedLength, Model.Start, Model.Range.End)
            : (roundedStart, roundedLength);
        int zindex = Timeline.ToLayerNumber(Margin.Value);

        var request = new ElementResizeRequest(Model, start, length, zindex);
        Timeline.EditorContext.GetRequiredService<IElementResizeService>()
            .Resize(Scene, [request], ripple);

        await AnimationRequest(context);
    }

    public PrepareAnimationContext PrepareAnimation()
    {
        return new PrepareAnimationContext(
            Margin: Margin.Value,
            BorderMargin: BorderMargin.Value,
            Width: Width.Value,
            Inlines: Timeline.Inlines
                .Where(x => x.Element == this)
                .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
                .ToArray(),
            Scope: Scope.PrepareAnimation());
    }

    private void OnThumbnailsDisabledElementsAttached(Guid id)
    {
        if (id == Model.Id && !IsThumbnailsDisabled.Value)
        {
            IsThumbnailsDisabled.Value = true;
        }
    }

    private void OnThumbnailsDisabledElementsDetached(Guid id)
    {
        if (id == Model.Id && IsThumbnailsDisabled.Value)
        {
            IsThumbnailsDisabled.Value = false;
        }
    }

    private void OnExclude()
    {
        Element[] targets = EditableTargets();
        if (targets.Length == 0) return;
        Timeline.EditorContext.GetRequiredService<IElementStructureService>()
            .Exclude(Scene, targets, Timeline.IsRippleEnabled.Value);
    }

    private void OnDelete()
    {
        Element[] targets = EditableTargets();
        if (targets.Length == 0) return;
        Timeline.EditorContext.GetRequiredService<IElementStructureService>()
            .Delete(Scene, targets, Timeline.IsRippleEnabled.Value);
    }

    private Element[] EditableTargets()
        => GetGroupOrSelectedElements().Where(e => e.IsEditable.Value).Select(e => e.Model).ToArray();

    private void OnBringAnimationToTop()
    {
        if (LayerHeader.Value is { } layerHeader)
        {
            InlineAnimationLayerViewModel[] inlines = Timeline.Inlines.Where(x => x.Element == this).ToArray();
            Array.Sort(inlines, (x, y) => x.Index.Value - y.Index.Value);

            for (int i = 0; i < inlines.Length; i++)
            {
                InlineAnimationLayerViewModel? item = inlines[i];
                int oldIndex = layerHeader.Inlines.IndexOf(item);
                if (oldIndex >= 0)
                {
                    layerHeader.Inlines.Move(oldIndex, i);
                }
            }
        }
    }

    private void OnFinishEditingAnimation()
    {
        if (!IsEditable.Value) return;
        foreach (InlineAnimationLayerViewModel item in Timeline.Inlines.Where(x => x.Element == this).ToArray())
        {
            Timeline.DetachInline(item);
        }
    }

    private HashSet<Guid> GetEditableSelectedIdsOrSelf()
    {
        var ids = new HashSet<Guid>(Timeline.SelectedElements
            .Where(x => x.IsEditable.Value)
            .Select(x => x.Model.Id));

        if (ids.Count == 0 && IsEditable.Value)
        {
            ids.Add(Model.Id);
        }

        return ids;
    }

    private void OnGroupSelectedElements()
    {
        // No self-editability guard: a mixed selection dispatched through a
        // locked first clip must still group its editable members.
        IReadOnlyCollection<Guid> ids = GetEditableSelectedIdsOrSelf();
        if (ids.Count == 0) return;
        Timeline.EditorContext.GetRequiredService<IElementStructureService>().Group(Scene, ids);
    }

    private void OnUngroupSelectedElements()
    {
        IReadOnlyCollection<Guid> ids = GetEditableSelectedIdsOrSelf();
        if (ids.Count == 0) return;
        Timeline.EditorContext.GetRequiredService<IElementStructureService>().Ungroup(Scene, ids);
    }

    private void OnSetAttached(ImmutableHashSet<Guid> group)
    {
        if (group.Contains(Model.Id))
        {
            _elementGroup = group;
        }
    }

    private void OnSetDetached(ImmutableHashSet<Guid> group)
    {
        if (ReferenceEquals(_elementGroup, group))
        {
            _elementGroup = null;
        }
    }

    private async Task OnCopy()
    {
        Element[] models = GetGroupOrSelectedElements().Select(e => e.Model).ToArray();
        await Timeline.EditorContext.GetRequiredService<IElementClipboardService>().CopyAsync(models);
    }

    private async Task OnCut()
    {
        Element[] models = EditableTargets();
        if (models.Length == 0) return;
        await Timeline.EditorContext.GetRequiredService<IElementClipboardService>()
            .CutAsync(Scene, models, Timeline.IsRippleEnabled.Value);
    }

    public void SplitAt(TimeSpan timeSpan)
    {
        SplitCore([this], timeSpan);
    }

    internal void SplitAt(IReadOnlyList<ElementViewModel> targets, TimeSpan timeSpan)
    {
        SplitCore(targets, timeSpan);
    }

    private void OnSplit(TimeSpan timeSpan)
    {
        IReadOnlyList<ElementViewModel> targets = GetGroupOrSelectedElements();
        if (targets.Count == 0)
        {
            targets = [this];
        }

        SplitCore(targets, timeSpan);
    }

    private void SplitCore(IReadOnlyList<ElementViewModel> targets, TimeSpan timeSpan)
    {
        Element[] models = targets.Where(t => t.IsEditable.Value).Select(t => t.Model).ToArray();
        if (models.Length == 0) return;
        int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan at = timeSpan.RoundToRate(rate);

        Timeline.EditorContext.GetRequiredService<IElementStructureService>().Split(Scene, models, at);
    }

    private async void OnChangeToOriginalDuration()
    {
        if (!IsEditable.Value) return;
        if (Model.HasOriginalDuration()
            && Model.TryGetOriginalDuration(out TimeSpan timeSpan))
        {
            PrepareAnimationContext context = PrepareAnimation();

            int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan duration = timeSpan.FloorToRate(rate);

            bool ripple = Timeline.IsRippleEnabled.Value;
            Element? after = Model.GetAfter(Model.ZIndex, Model.Range.End);
            if (!ripple && after != null)
            {
                TimeSpan delta = after.Start - Model.Start;
                if (delta < duration)
                {
                    duration = delta;
                }
            }

            var request = new ElementResizeRequest(Model, Model.Start, duration, Model.ZIndex);
            Timeline.EditorContext.GetRequiredService<IElementResizeService>()
                .Resize(Scene, [request], ripple);

            await AnimationRequest(context);
        }
    }

    public bool HasOriginalDuration()
    {
        return Model.HasOriginalDuration();
    }

    public void Execute(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;
        switch (execution.CommandName)
        {
            case "Rename":
                if (IsEditable.Value)
                {
                    RenameRequested();
                }
                break;
            case "Split":
                SplitByCurrentFrame.Execute();
                break;
            default:
                if (execution.KeyEventArgs != null)
                    execution.KeyEventArgs.Handled = false;
                break;
        }
    }

    public record struct PrepareAnimationContext(
        Thickness Margin,
        Thickness BorderMargin,
        double Width,
        (InlineAnimationLayerViewModel ViewModel, InlineAnimationLayerViewModel.PrepareAnimationContext Context)[]
            Inlines,
        ElementScopeViewModel.PrepareAnimationContext Scope);

    private void CancelThumbnailsLoading()
    {
        _thumbnailsCts?.Cancel();
        _thumbnailsCts?.Dispose();
        _thumbnailsCts = null;
    }

    private async void UpdateThumbnailsAsync()
    {
        // プレビューが無効化されている場合は何もしない
        if (IsThumbnailsDisabled.Value)
        {
            CancelThumbnailsLoading();
            ThumbnailsKind.Value = Engine.ThumbnailsKind.None;
            ThumbnailsClear?.Invoke();
            WaveformClear?.Invoke();
            return;
        }

        var provider = FindThumbnailsProvider();

        // プロバイダーが変更された場合、イベント購読を更新
        if (_currentThumbnailsProvider != provider)
        {
            if (_currentThumbnailsProvider != null && _thumbnailsInvalidatedHandler != null)
            {
                _currentThumbnailsProvider.ThumbnailsInvalidated -= _thumbnailsInvalidatedHandler;
            }

            _currentThumbnailsProvider = provider;
            _lastThumbnailsCacheKey = provider?.GetThumbnailsCacheKey();

            if (provider != null)
            {
                _thumbnailsInvalidatedHandler = (_, _) =>
                {
                    // 旧キーでキャッシュを無効化
                    var oldKey = _lastThumbnailsCacheKey;
                    if (oldKey != null)
                        _thumbnailCacheService.Invalidate(oldKey);

                    // 新しいキーをキャプチャ
                    _lastThumbnailsCacheKey = _currentThumbnailsProvider?.GetThumbnailsCacheKey();

                    _thumbnailsInvalidatedSubject.OnNext(Unit.Default);
                };
                provider.ThumbnailsInvalidated += _thumbnailsInvalidatedHandler;
            }
        }

        if (provider == null)
        {
            ThumbnailsKind.Value = Engine.ThumbnailsKind.None;
            return;
        }

        ThumbnailsKind.Value = provider.ThumbnailsKind;

        CancelThumbnailsLoading();
        _thumbnailsCts = new CancellationTokenSource();
        var ct = _thumbnailsCts.Token;

        try
        {
            switch (provider.ThumbnailsKind)
            {
                case Engine.ThumbnailsKind.Video:
                    await UpdateVideoThumbnailsAsync(provider, ct);
                    break;
                case Engine.ThumbnailsKind.Audio:
                    await UpdateAudioThumbnailsAsync(provider, ct);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update thumbnails.");
        }
        finally
        {
            // 生成完了後、このCTSがまだ現役なら解放する
            if (_thumbnailsCts is { } cts && cts.Token == ct)
            {
                _thumbnailsCts = null;
                cts.Dispose();
            }
        }
    }

    private IThumbnailsProvider? FindThumbnailsProvider()
    {
        foreach (var child in Model.Objects)
        {
            if (child is IThumbnailsProvider provider)
                return provider;
        }

        return null;
    }

    private static readonly IBrush s_proxyReadyBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly IBrush s_proxyGeneratingBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x21, 0x96, 0xF3));
    private static readonly IBrush s_proxyStaleBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly IBrush s_proxyFailedBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0xF4, 0x43, 0x36));
    private static readonly IBrush s_proxyNoneBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x9E, 0x9E, 0x9E));

    private bool PreferProxyForThumbnails => Scene.PreviewSourceMode == PreviewSourceMode.PreferProxy;

    private IAsyncEnumerable<(int Index, int Count, Beutl.Media.Bitmap Thumbnail)> GetVideoThumbnailStrip(
        IThumbnailsProvider provider, int width, int height, CancellationToken ct, int start, int end)
    {
        return provider is Beutl.Graphics.SourceVideo video
            ? video.GetThumbnailStripAsync(width, height, _thumbnailCacheService, ct, start, end, PreferProxyForThumbnails)
            : provider.GetThumbnailStripAsync(width, height, _thumbnailCacheService, ct, start, end);
    }

    private VideoSource? FindVideoSource()
    {
        // Cover every proxy-aware holder (VideoSourceNode graph inputs, referenced scenes, animated
        // values) so an element that uses video only through those paths still shows the badge, not
        // just a top-level SourceVideo's current value.
        return ProxySourceEnumerator.EnumerateVideoSources(Model).FirstOrDefault();
    }

    private ProxyFingerprint? ResolveProxyFingerprint(bool invalidateCache = false)
    {
        VideoSource? source = FindVideoSource();
        Uri? uri = source is { HasUri: true } && source.Uri is { IsFile: true } fileUri ? fileUri : null;

        return ResolveCachedFingerprint(
            uri,
            invalidateCache,
            ref _proxySourceUri,
            ref _proxyFingerprint,
            static u => ProxyFingerprint.TryFromFile(u.LocalPath, out ProxyFingerprint fingerprint)
                ? fingerprint
                : null);
    }

    // Keyed on the URI so high-frequency store/queue refreshes never re-stat the file; an in-place
    // overwrite keeps the URI, so those callers pass invalidateCache to force one re-stat.
    internal static ProxyFingerprint? ResolveCachedFingerprint(
        Uri? currentUri,
        bool invalidateCache,
        ref Uri? cachedUri,
        ref ProxyFingerprint? cachedFingerprint,
        Func<Uri, ProxyFingerprint?> stat)
    {
        if (currentUri is null)
        {
            cachedUri = null;
            cachedFingerprint = null;
        }
        else if (invalidateCache || !Equals(currentUri, cachedUri))
        {
            cachedUri = currentUri;
            cachedFingerprint = stat(currentUri);
        }

        return cachedFingerprint;
    }

    private void OnProxyStateInvalidated()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(OnProxyStateInvalidated);
            return;
        }

        RefreshProxyState();
    }

    private void RefreshProxyState(bool invalidateFingerprintCache = false)
    {
        if (_proxyStore is not { } store || ResolveProxyFingerprint(invalidateFingerprintCache) is not { } fingerprint)
        {
            ShowProxyIndicator.Value = false;
            ProxyIndicatorState.Value = ProxyState.None;
            return;
        }

        ProxyIndicatorState.Value = ResolveProxyState(store, _proxyJobQueue, fingerprint);
        ShowProxyIndicator.Value = true;
    }

    internal static ProxyState ResolveProxyState(IProxyStore store, IProxyJobQueue? queue, ProxyFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (queue != null)
        {
            foreach (ProxyJob job in queue.Pending())
            {
                if (string.Equals(job.Source.AbsolutePath, fingerprint.AbsolutePath, StringComparison.Ordinal))
                    return ProxyState.Generating;
            }
        }

        ProxyState? best = null;
        foreach (ProxyEntry entry in store.Enumerate())
        {
            if (!string.Equals(entry.Source.AbsolutePath, fingerprint.AbsolutePath, StringComparison.Ordinal))
                continue;

            ProxyState effective = entry.Source == fingerprint ? entry.State : ProxyState.Stale;
            best = best is { } current && ProxyStateRank(current) >= ProxyStateRank(effective)
                ? current
                : effective;
        }

        return best ?? ProxyState.None;
    }

    private static int ProxyStateRank(ProxyState state) => state switch
    {
        ProxyState.Ready => 5,
        ProxyState.Generating => 4,
        ProxyState.Stale => 3,
        ProxyState.Partial => 2,
        ProxyState.Failed => 1,
        _ => 0,
    };

    private static IBrush GetProxyStateBrush(ProxyState state) => state switch
    {
        ProxyState.Ready => s_proxyReadyBrush,
        ProxyState.Generating => s_proxyGeneratingBrush,
        ProxyState.Stale or ProxyState.Partial => s_proxyStaleBrush,
        ProxyState.Failed => s_proxyFailedBrush,
        _ => s_proxyNoneBrush,
    };

    private static string GetProxyStateText(ProxyState state) => state switch
    {
        ProxyState.Ready => Strings.ProxyReady,
        ProxyState.Generating => Strings.ProxyGenerating,
        ProxyState.Stale => Strings.ProxyStale,
        ProxyState.Partial => Strings.ProxyPartial,
        ProxyState.Failed => Strings.ProxyFailed,
        _ => Strings.ProxyMissing,
    };

    public void OnVisibleRangeChanged(int start, int end)
    {
        _lastVisibleStart = start;
        _lastVisibleEnd = end;
        _visibleRangeSubject.OnNext((start, end));
    }

    private async Task UpdateVisibleThumbnailsAsync(int start, int end)
    {
        if (end < start) return;

        var provider = _currentThumbnailsProvider;
        if (provider == null || provider.ThumbnailsKind != Engine.ThumbnailsKind.Video)
            return;

        // Width変更による生成が進行中ならキャンセルする
        CancelThumbnailsLoading();

        var missing = GetMissingThumbnailIndices?.Invoke(start, end);
        if (missing == null || missing.Count == 0)
            return;

        _scrollThumbnailsCts?.Cancel();
        _scrollThumbnailsCts?.Dispose();
        _scrollThumbnailsCts = new CancellationTokenSource();
        var ct = _scrollThumbnailsCts.Token;

        try
        {
            const int MaxThumbnailHeight = 25;
            double width = Width.Value;
            if (width <= 0)
                return;

            await foreach (var (index, count, thumbnail) in GetVideoThumbnailStrip(
                provider, (int)width, MaxThumbnailHeight, ct, start, end))
            {
                if (ct.IsCancellationRequested)
                {
                    thumbnail.Dispose();
                    break;
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using (thumbnail)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            VideoThumbnailCount.Value = count;
                            ThumbnailReady?.Invoke(index, !thumbnail.IsDisposed ? thumbnail.ToAvaWriteableBitmap(null) : null);
                        }
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update visible thumbnails.");
        }
        finally
        {
            _scrollThumbnailsCts?.Dispose();
            _scrollThumbnailsCts = null;
        }
    }

    private async Task UpdateVideoThumbnailsAsync(IThumbnailsProvider provider, CancellationToken ct)
    {
        const int MaxThumbnailHeight = 25;
        double width = Width.Value;
        if (width <= 0)
            return;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ct.IsCancellationRequested)
            {
                ThumbnailsClear?.Invoke();
            }
        });

        int startIndex = _lastVisibleStart >= 0 ? _lastVisibleStart : 0;
        int endIndex = _lastVisibleEnd >= 0 ? _lastVisibleEnd : -1;

        await foreach (var (index, count, thumbnail) in GetVideoThumbnailStrip(provider, (int)width, MaxThumbnailHeight, ct, startIndex, endIndex))
        {
            if (ct.IsCancellationRequested)
            {
                thumbnail.Dispose();
                break;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                using (thumbnail)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        VideoThumbnailCount.Value = count;
                        ThumbnailReady?.Invoke(index, !thumbnail.IsDisposed ? thumbnail.ToAvaWriteableBitmap(null) : null);
                    }
                }
            });
        }
    }

    private async Task UpdateAudioThumbnailsAsync(IThumbnailsProvider provider, CancellationToken ct)
    {
        const int MaxSamplesPerChunk = 4096;
        double width = Width.Value;
        if (width <= 0)
            return;

        int chunkCount = Math.Max(1, (int)width);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ct.IsCancellationRequested)
            {
                WaveformClear?.Invoke();
                WaveformChunkCount.Value = chunkCount;
            }
        });

        await foreach (var chunk in provider.GetWaveformChunksAsync(chunkCount, MaxSamplesPerChunk, _thumbnailCacheService, ct))
        {
            if (ct.IsCancellationRequested)
                break;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    WaveformChunkReady?.Invoke(chunk);
                }
            });
        }
    }
}
