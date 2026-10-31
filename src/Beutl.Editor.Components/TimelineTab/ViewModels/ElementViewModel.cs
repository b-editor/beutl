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
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;
using FluentAvalonia.UI.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class ElementViewModel : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<ElementViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly Subject<Unit> _thumbnailsInvalidatedSubject = new();
    private readonly Subject<Unit> _elementEditedSubject = new();
    private CancellationTokenSource? _thumbnailsCts;
    private IThumbnailsProvider? _currentThumbnailsProvider;
    private readonly IThumbnailCacheService _thumbnailCacheService = ThumbnailCacheService.Instance;
    private EventHandler? _thumbnailsInvalidatedHandler;
    private string? _lastThumbnailsCacheKey;
    private int _lastVisibleStart = -1;
    private int _lastVisibleEnd = -1;
    private CancellationTokenSource? _scrollThumbnailsCts;

    // The proxy store/queue handlers marshal via Dispatcher.UIThread.Post, so a posted callback can
    // run after Dispose; they bail on this flag instead of touching disposed reactive state.
    private bool _isDisposed;
    private bool _viewStateInitialized;
    private readonly Subject<(int Start, int End)> _visibleRangeSubject = new();
    private readonly IProxyStore? _proxyStore;
    private readonly IProxyJobQueue? _proxyJobQueue;
    private string? _proxySourceKey;
    private IReadOnlyList<ProxyFingerprint> _proxyFingerprints = [];

    public Func<int, int, List<int>>? GetMissingThumbnailIndices;

    public ElementViewModel(Element element, TimelineTabViewModel timeline)
    {
        Model = element;
        Timeline = timeline;
        Scene = timeline.Scene;

        InitializeElementGroup();

        IsEnabled = element.IsEnabled.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsLocked = element.IsLocked.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Name = element.Name.GetObservable()
            .ToReactiveProperty()
            .AddTo(_disposables)!;

        IObservable<int> zIndexSubject = element.ZIndex.GetObservable();
        Margin = Timeline.GetTrackedLayerTopObservable(zIndexSubject)
            .Select(item => new Thickness(0, item, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        BorderMargin = element.Start.GetObservable()
            .CombineLatest(timeline.Scale)
            .Select(item => new Thickness(item.First.TimeToPixel(item.Second), 0, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width = element.Length.GetObservable()
            .CombineLatest(timeline.Scale)
            .Select(item => item.First.TimeToPixel(item.Second))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Color = element.AccentColor.GetObservable()
            .Select(c => c.ToAvaColor())
            .ToReactiveProperty()
            .AddTo(_disposables);

        RestBorderColor = Color.Select(v => (Avalonia.Media.Color)((Color2)v).LightenPercent(-0.3f))
            .ToReadOnlyReactivePropertySlim();

        TextColor = Color.Select(ColorGenerator.GetTextColor)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Split.Where(_ => GetClickedTime != null)
            .Subscribe(_ => OnSplit(GetClickedTime!()))
            .AddTo(_disposables);

        SplitByCurrentFrame
            .Subscribe(_ => OnSplit(timeline.CurrentTime.Value))
            .AddTo(_disposables);

        Cut.Subscribe(OnCut)
            .AddTo(_disposables);

        Copy = new AsyncReactiveCommand()
            .WithSubscribe(OnCopy)
            .DisposeWith(_disposables);

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

        zIndexSubject.Subscribe(number =>
            {
                // A header's number is its index in LayerHeaders.
                LayerHeaderViewModel? newLH = number >= 0 && number < Timeline.LayerHeaders.Count
                    ? Timeline.LayerHeaders[number]
                    : null;

                LayerHeader.Value?.ElementRemoved();

                newLH?.ElementAdded();
                LayerHeader.Value = newLH;
            })
            .AddTo(_disposables);

        // The user's locks: the element's own or its TimelineLayer's.
        IsLockedByUser = IsLocked
            .CombineLatest(
                LayerHeader.Select(h => h is null
                    ? Observable.Return(false)
                    : h.IsLocked).Switch(),
                (el, layer) => el || layer)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        IsRecoveryProtected = Observable.FromEventPattern(
                handler => element.RecoveryProtectionChanged += handler,
                handler => element.RecoveryProtectionChanged -= handler)
            .Select(_ => element.IsRecoveryProtected)
            .ToReadOnlyReactivePropertySlim(element.IsRecoveryProtected)
            .AddTo(_disposables);

        // Editable unless it is locked, or recovery keeps its file because part of it could not be
        // loaded (saving would not write the edits).
        IsEditable = IsLockedByUser
            .CombineLatest(IsRecoveryProtected, (locked, isProtected) => !locked && !isProtected)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        // Locking a protected clip would not be saved and would block its repair (see
        // ElementAttributeService.SetLocked); unlocking one stays possible.
        CanToggleLock = IsLocked
            .CombineLatest(IsRecoveryProtected, (locked, isProtected) => locked || !isProtected)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        RecoveryProtectionToolTip = IsRecoveryProtected
            .Select(isProtected => isProtected ? MessageStrings.RecoveryProtectedElementToolTip : null)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        // Must stay after IsEditable is assigned — the handler reads it. Skip(1) drops the initial
        // sync emit; while locked a rename must not persist, so snap Name.Value back to Model.Name.
        Name.Skip(1)
            .Subscribe(v =>
            {
                if (IsEditable.Value)
                    Timeline.EditorContext.GetRequiredService<IElementAttributeService>().SetName(Model, v);
                else if (v != Model.Name.Value) Name.Value = Model.Name.Value;
            })
            .AddTo(_disposables);

        Scope = new ElementScopeViewModel(Model, this);

        _enterPart.AddTo(_disposables);
        _exitPart.AddTo(_disposables);
        MatteIndicator.AddTo(_disposables);

        IsThumbnailsKindAudio = ThumbnailsKind.Select(k => k == Engine.ThumbnailsKind.Audio)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        IsThumbnailsKindVideo = ThumbnailsKind.Select(k => k == Engine.ThumbnailsKind.Video)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        _proxyStore = Timeline.EditorContext.GetService<IProxyStore>();
        _proxyJobQueue = Timeline.EditorContext.GetService<IProxyJobQueue>();
        ProxyIndicatorBrush = ProxyIndicatorState
            .Select(GetProxyStateBrush)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables)!;
        ProxyIndicatorTooltip = ProxyIndicatorState
            .Select(GetProxyStateText)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables)!;
    }

    internal void InitializeViewState()
    {
        if (_isDisposed || _viewStateInitialized) return;
        _viewStateInitialized = true;
        InitializeTransition();
        InitializeTrackMatte();
        InitializeThumbnails();
        InitializeProxyIndicator();
    }

    private void InitializeElementGroup()
    {
        GroupSelectedElements.Subscribe(_ => OnGroupSelectedElements())
            .AddTo(_disposables);

        UngroupSelectedElements.Subscribe(_ => OnUngroupSelectedElements())
            .AddTo(_disposables);
    }

    public Func<(Thickness Margin, Thickness BorderMargin, double Width), CancellationToken, Task> AnimationRequested
    {
        get;
        set;
    } = (_, _) => Task.CompletedTask;

    public Action RenameRequested { get; set; } = () => { };

    public Func<MediaTime>? GetClickedTime { get; set; }

    public TimelineTabViewModel Timeline { get; }

    public Element Model { get; }

    public ElementScopeViewModel Scope { get; }

    public Scene Scene { get; }

    public ReadOnlyReactivePropertySlim<bool> IsEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLocked { get; }

    public ReadOnlyReactivePropertySlim<bool> IsLockedByUser { get; }

    // True while recovery keeps the element's original file (Element.IsRecoveryProtected).
    public ReadOnlyReactivePropertySlim<bool> IsRecoveryProtected { get; }

    public ReadOnlyReactivePropertySlim<bool> IsEditable { get; }

    public ReadOnlyReactivePropertySlim<bool> CanToggleLock { get; }

    public ReadOnlyReactivePropertySlim<string?> RecoveryProtectionToolTip { get; }

    public ReactiveProperty<string> Name { get; }

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> BorderMargin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveProperty<bool> IsSelected { get; } = new(false);

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader { get; } = new();

    public ReactiveProperty<Avalonia.Media.Color> Color { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> RestBorderColor { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> TextColor { get; }

    public ReactiveCommand Split { get; } = new();

    public ReactiveCommand SplitByCurrentFrame { get; } = new();

    public AsyncReactiveCommand Cut { get; } = new();

    public AsyncReactiveCommand Copy { get; }

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
        HashSet<Guid> ids = DuplicateHelper.ExpandWithGroupSiblings(
            Timeline.SelectedElements.Select(item => item.Model.Id.Value), Scene.Groups);

        // Group membership is only needed for an interaction. Per-element subscriptions make
        // bulk split groups notify every timeline element for every newly created group.
        if (Scene.Groups.FirstOrDefault(g => g.Contains(Model.Id.Value)) is { } group)
        {
            ids.UnionWith(group);
        }

        if (ids.Count == 0)
        {
            return [];
        }

        return Timeline.Elements
            .Where(x => ids.Contains(x.Model.Id.Value))
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
        _isDisposed = true;

        if (_currentThumbnailsProvider != null && _thumbnailsInvalidatedHandler != null)
        {
            _currentThumbnailsProvider.ThumbnailsInvalidated -= _thumbnailsInvalidatedHandler;
        }
        _thumbnailsInvalidatedSubject.Dispose();
        _elementEditedSubject.Dispose();
        _visibleRangeSubject.Dispose();

        CancelThumbnailsLoading();

        _scrollThumbnailsCts?.Cancel();
        _scrollThumbnailsCts?.Dispose();
        _scrollThumbnailsCts = null;
        if (_lastThumbnailsCacheKey != null)
            InvalidateAllThumbnailCacheKeys(_thumbnailCacheService, _lastThumbnailsCacheKey);

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
    }

    public async void AnimationRequest(int layerNum)
    {
        var inlines = Timeline.Inlines
            .Where(x => x.Element == this)
            .Select(x => (ViewModel: x, Context: x.PrepareAnimation()))
            .ToArray();
        var scope = Scope.PrepareAnimation();

        Thickness newMargin = new(0, Timeline.CalculateLayerTop(layerNum), 0, 0);

        foreach (var (item, context) in inlines)
            item.AnimationRequest(context, newMargin, BorderMargin.Value);

        Task task1 = Scope.AnimationRequest(scope);
        Task task2 = AnimationRequested((newMargin, BorderMargin.Value, Width.Value), CancellationToken.None);

        await Task.WhenAll(task1, task2);
        Margin.Value = newMargin;
    }

    public async Task AnimationRequest(PrepareAnimationContext context, CancellationToken cancellationToken = default)
    {
        var margin = new Thickness(0, Timeline.CalculateLayerTop(Model.ZIndex.Value), 0, 0);
        var borderMargin = new Thickness(Model.Start.Value.TimeToPixel(Timeline.Options.Value.Scale), 0, 0, 0);
        double width = Model.Length.Value.TimeToPixel(Timeline.Options.Value.Scale);

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
    internal static (MediaTime Start, MediaTime Length) ResolveRippleResizeBounds(
        bool leftEdge, MediaTime roundedStart, MediaTime roundedLength, MediaTime modelStart, MediaTime modelEnd)
    {
        return leftEdge
            ? (roundedStart, modelEnd - roundedStart)
            : (modelStart, roundedLength);
    }

    public async Task SubmitViewModelChanges(bool ripple = false, bool leftEdge = false)
    {
        PrepareAnimationContext context = PrepareAnimation();

        float scale = Timeline.Options.Value.Scale;
        Rational rate = SceneTimeRangeService.GetFrameRate(Scene);
        MediaTime roundedStart = BorderMargin.Value.Left.PixelToTime(scale).RoundToFrame(rate, MidpointRounding.AwayFromZero);
        MediaTime roundedLength = Width.Value.PixelToTime(scale).RoundToFrame(rate, MidpointRounding.AwayFromZero);
        (MediaTime start, MediaTime length) = ripple || leftEdge
            ? ResolveRippleResizeBounds(leftEdge, roundedStart, roundedLength, Model.Start.Value, Model.Range.End)
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

    private void OnDelete()
    {
        Element[] targets = RemovableTargets();
        if (targets.Length == 0) return;
        Timeline.EditorContext.GetRequiredService<IElementStructureService>()
            .Delete(Scene, targets, Timeline.IsRippleEnabled.Value);
    }

    private Element[] EditableTargets()
        => GetGroupOrSelectedElements().Where(e => e.IsEditable.Value).Select(e => e.Model).ToArray();

    // IElementStructureService skips the user-locked ones.
    private Element[] RemovableTargets()
        => Timeline.EditorContext.GetRequiredService<IEditorSelection>().GetTargetElements(Scene);

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
            .Select(x => x.Model.Id.Value));

        if (ids.Count == 0 && IsEditable.Value)
        {
            ids.Add(Model.Id.Value);
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

    public void SplitAt(MediaTime timeSpan)
    {
        SplitCore([this], timeSpan);
    }

    private void OnSplit(MediaTime timeSpan)
    {
        IReadOnlyList<ElementViewModel> targets = GetGroupOrSelectedElements();
        if (targets.Count == 0)
        {
            targets = [this];
        }

        SplitCore(targets, timeSpan);
    }

    private void SplitCore(IReadOnlyList<ElementViewModel> targets, MediaTime timeSpan)
    {
        Element[] models = targets.Where(t => t.IsEditable.Value).Select(t => t.Model).ToArray();
        if (models.Length == 0) return;
        Rational rate = SceneTimeRangeService.GetFrameRate(Scene);
        MediaTime at = timeSpan.RoundToFrame(rate, MidpointRounding.AwayFromZero);

        Timeline.EditorContext.GetRequiredService<IElementStructureService>().Split(Scene, models, at);
    }

    private async void OnChangeToOriginalDuration()
    {
        if (!IsEditable.Value) return;

        PrepareAnimationContext context = PrepareAnimation();
        if (Timeline.EditorContext.GetRequiredService<IElementResizeService>()
            .ResizeToOriginalDuration(Scene, Model, Timeline.IsRippleEnabled.Value))
        {
            await AnimationRequest(context);
        }
    }

    public bool HasOriginalDuration()
    {
        return SlippableMedia.HasOriginalDuration(Model);
    }

    public record struct PrepareAnimationContext(
        Thickness Margin,
        Thickness BorderMargin,
        double Width,
        (InlineAnimationLayerViewModel ViewModel, InlineAnimationLayerViewModel.PrepareAnimationContext Context)[]
            Inlines,
        ElementScopeViewModel.PrepareAnimationContext Scope);

    private static readonly IBrush s_proxyReadyBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly IBrush s_proxyGeneratingBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x21, 0x96, 0xF3));
    private static readonly IBrush s_proxyStaleBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly IBrush s_proxyFailedBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0xF4, 0x43, 0x36));
    private static readonly IBrush s_proxyNoneBrush = new ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x9E, 0x9E, 0x9E));
}
