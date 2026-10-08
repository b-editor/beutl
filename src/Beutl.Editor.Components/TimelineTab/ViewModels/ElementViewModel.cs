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

public sealed partial class ElementViewModel : IDisposable, IContextCommandHandler
{
    private readonly ILogger _logger = Log.CreateLogger<ElementViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private ImmutableHashSet<Guid>? _elementGroup;
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

        RestBorderColor = Color.Select(v => (Avalonia.Media.Color)((Color2)v).LightenPercent(-0.3f))
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

        Copy = new AsyncReactiveCommand()
            .WithSubscribe(OnCopy)
            .DisposeWith(_disposables);

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

        // Must stay after IsEditable is assigned — the handler reads it. Skip(1) drops the initial
        // sync emit; while locked a rename must not persist, so snap Name.Value back to Model.Name.
        Name.Skip(1)
            .Subscribe(v =>
            {
                if (IsEditable.Value)
                    Timeline.EditorContext.GetRequiredService<IElementAttributeService>().SetName(Model, v);
                else if (v != Model.Name) Name.Value = Model.Name;
            })
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
            .AddTo(_disposables)!;
        ProxyIndicatorTooltip = ProxyIndicatorState
            .Select(GetProxyStateText)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables)!;
        InitializeProxyIndicator();
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

    public AsyncReactiveCommand Copy { get; }

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
        _isDisposed = true;

        // ThumbnailsInvalidatedイベントの購読を解除
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
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan roundedStart = BorderMargin.Value.Left.PixelToTimeSpan(scale).RoundToRate(rate);
        TimeSpan roundedLength = Width.Value.PixelToTimeSpan(scale).RoundToRate(rate);
        (TimeSpan start, TimeSpan length) = ripple || leftEdge
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
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan at = timeSpan.RoundToRate(rate);

        Timeline.EditorContext.GetRequiredService<IElementStructureService>().Split(Scene, models, at);
    }

    private async void OnChangeToOriginalDuration()
    {
        if (!IsEditable.Value) return;
        if (SlippableMedia.GetOriginalDuration(Model) is { } timeSpan)
        {
            PrepareAnimationContext context = PrepareAnimation();

            int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
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

            var request = new ElementResizeRequest(Model, Model.Start, duration, Model.ZIndex) { ClampToSource = true };
            Timeline.EditorContext.GetRequiredService<IElementResizeService>()
                .Resize(Scene, [request], ripple);

            await AnimationRequest(context);
        }
    }

    public bool HasOriginalDuration()
    {
        return SlippableMedia.HasOriginalDuration(Model);
    }

    public Task ExecuteAsync(ContextCommandExecution execution)
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

        return Task.CompletedTask;
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
