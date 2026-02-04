using System.Collections.Immutable;
using System.Reactive;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Beutl.Animation;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Operation;
using Microsoft.Extensions.DependencyInjection;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;
using FluentAvalonia.UI.Media;
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
    private IElementThumbnailsProvider? _currentThumbnailsProvider;
    private EventHandler? _thumbnailsInvalidatedHandler;

    public ElementViewModel(Element element, TimelineTabViewModel timeline)
    {
        Model = element;
        Timeline = timeline;

        InitializeElementGroup();

        // プロパティを構成
        IsEnabled = element.GetObservable(Element.IsEnabledProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        UseNode = element.GetObservable(Element.UseNodeProperty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Name = element.GetObservable(CoreObject.NameProperty)
            .ToReactiveProperty()
            .AddTo(_disposables)!;
        Name.Subscribe(v => Model.Name = v)
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

        Copy.Subscribe(async () => await SetClipboard([.. GetGroupOrSelectedElements()]))
            .AddTo(_disposables);

        Exclude.Subscribe(OnExclude)
            .AddTo(_disposables);

        Delete.Subscribe(OnDelete)
            .AddTo(_disposables);

        Color.Skip(1)
            .Subscribe(c =>
            {
                Model.AccentColor = c.ToBtlColor();
                Timeline.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.ChangeElementColor);
            })
            .AddTo(_disposables);

        FinishEditingAnimation.Subscribe(OnFinishEditingAnimation)
            .AddTo(_disposables);

        BringAnimationToTop.Subscribe(OnBringAnimationToTop)
            .AddTo(_disposables);

        ChangeToOriginalLength.Subscribe(OnChangeToOriginalLength)
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

        Scope = new ElementScopeViewModel(Model, this);

        // プレビュー関連の初期化
        IsThumbnailsKindAudio = ThumbnailsKind.Select(k => k == ElementThumbnailsKind.Audio)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        IsThumbnailsKindVideo = ThumbnailsKind.Select(k => k == ElementThumbnailsKind.Video)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        InitializeThumbnails();
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

    public TimelineTabViewModel Timeline { get; private set; }

    public Element Model { get; private set; }

    public ElementScopeViewModel Scope { get; private set; }

    public Scene Scene => Model.HierarchicalParent as Scene ?? Timeline.Scene;

    public ReadOnlyReactivePropertySlim<bool> IsEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> UseNode { get; }

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

    public ReactiveCommand ChangeToOriginalLength { get; } = new();

    public ReactivePropertySlim<ElementThumbnailsKind> ThumbnailsKind { get; } = new(ElementThumbnailsKind.None);

    public ReadOnlyReactivePropertySlim<bool> IsThumbnailsKindVideo { get; }

    public ReadOnlyReactivePropertySlim<bool> IsThumbnailsKindAudio { get; }

    public ReactivePropertySlim<bool> IsThumbnailsDisabled { get; } = new();

    public ReactivePropertySlim<int> VideoThumbnailCount { get; } = new();

    public ReactivePropertySlim<int> WaveformChunkCount { get; } = new();

    public event Action<int, Bitmap?>? ThumbnailReady;

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
        IReadOnlyCollection<Guid> ids = GetSelectedIdsOrSelf();
        return ids.Count >= 2 && !Scene.Groups.Any(x => x.SetEquals(ids));
    }

    public bool CanUngroupSelectedElements()
    {
        IReadOnlyCollection<Guid> ids = GetSelectedIdsOrSelf();
        return Scene.Groups.Any(x => x.Overlaps(ids));
    }

    public void Dispose()
    {
        CancelThumbnailsLoading();

        // ThumbnailsInvalidatedイベントの購読を解除
        if (_currentThumbnailsProvider != null && _thumbnailsInvalidatedHandler != null)
        {
            _currentThumbnailsProvider.ThumbnailsInvalidated -= _thumbnailsInvalidatedHandler;
        }
        _thumbnailsInvalidatedSubject.Dispose();

        // ThumbnailsDisabledElementsイベントの購読を解除
        Timeline.ThumbnailsDisabledElements.Attached -= OnThumbnailsDisabledElementsAttached;
        Timeline.ThumbnailsDisabledElements.Detached -= OnThumbnailsDisabledElementsDetached;

        _disposables.Dispose();
        LayerHeader.Dispose();
        Scope.Dispose();

        ThumbnailsKind.Dispose();
        VideoThumbnailCount.Dispose();
        WaveformChunkCount.Dispose();

        LayerHeader.Value = null!;
        Timeline = null!;
        Model = null!;
        Scope = null!;
        AnimationRequested = (_, _) => Task.CompletedTask;
        GetClickedTime = null;
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

    public async Task SubmitViewModelChanges()
    {
        PrepareAnimationContext context = PrepareAnimation();

        float scale = Timeline.Options.Value.Scale;
        int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan start = BorderMargin.Value.Left.PixelToTimeSpan(scale).RoundToRate(rate);
        TimeSpan length = Width.Value.PixelToTimeSpan(scale).RoundToRate(rate);
        int zindex = Timeline.ToLayerNumber(Margin.Value);

        Scene.MoveChild(zindex, start, length, Model);
        Timeline.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.MoveElement);

        await AnimationRequest(context);
    }

    private async ValueTask<bool> SetClipboard(HashSet<ElementViewModel> selected)
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null) return false;

        var skipMulti = selected.Count == 1 && selected.First() == this;

        string singleJson = CoreSerializer.SerializeToJsonString(Model);
        string? multiJson = !skipMulti
            ? new JsonArray(selected
                    .Select(JsonNode (i) => CoreSerializer.SerializeToJsonObject(i.Model))
                    .ToArray())
                .ToJsonString()
            : null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(singleJson));
        data.Add(DataTransferItem.Create(BeutlDataFormats.Element, singleJson));
        if (!skipMulti)
        {
            data.Add(DataTransferItem.Create(BeutlDataFormats.Elements, multiJson));
        }

        await clipboard.SetDataAsync(data);
        return true;
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
        var history = Timeline.EditorContext.GetRequiredService<HistoryManager>();
        // IsSelectedがtrueのものをまとめて削除する。
        foreach (ElementViewModel element in GetGroupOrSelectedElements())
        {
            Scene.RemoveChild(element.Model);
        }
        history.Commit(CommandNames.RemoveElement);
    }

    private void OnDelete()
    {
        var history = Timeline.EditorContext.GetRequiredService<HistoryManager>();
        foreach (ElementViewModel element in GetGroupOrSelectedElements())
        {
            Scene.DeleteChild(element.Model);
        }
        history.Commit(CommandNames.DeleteElement);
    }

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
        foreach (InlineAnimationLayerViewModel item in Timeline.Inlines.Where(x => x.Element == this).ToArray())
        {
            Timeline.DetachInline(item);
        }
    }

    private HashSet<Guid> GetSelectedIdsOrSelf()
    {
        var ids = new HashSet<Guid>(Timeline.SelectedElements.Select(x => x.Model.Id));

        if (ids.Count == 0)
        {
            ids.Add(Model.Id);
        }

        return ids;
    }

    private void RemoveIdsFromElementSets(IReadOnlyCollection<Guid> ids)
    {
        for (int i = Scene.Groups.Count - 1; i >= 0; i--)
        {
            ImmutableHashSet<Guid> group = Scene.Groups[i];

            if (!group.Overlaps(ids))
                continue;

            ImmutableHashSet<Guid> updatedGroup = group.Except(ids);
            var index = i;
            if (updatedGroup.Count >= 2)
            {
                Scene.Groups[index] = updatedGroup;
            }
            else
            {
                Scene.Groups.RemoveAt(index);
            }
        }
    }

    private void OnGroupSelectedElements()
    {
        IReadOnlyCollection<Guid> ids = GetSelectedIdsOrSelf();

        RemoveIdsFromElementSets(ids);

        ImmutableHashSet<Guid> newGroup = [.. ids];
        if (newGroup.Count >= 2)
        {
            if (!Scene.Groups.Any(x => x.SetEquals(newGroup)))
            {
                Scene.Groups.Add(newGroup);
            }
        }

        Timeline.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.GroupElements);
    }

    private void OnUngroupSelectedElements()
    {
        IReadOnlyCollection<Guid> ids = GetSelectedIdsOrSelf();
        RemoveIdsFromElementSets(ids);

        Timeline.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.UngroupElements);
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

    private async Task OnCut()
    {
        var targets = GetGroupOrSelectedElements();

        if (targets.Count == 1 && ReferenceEquals(targets[0], this))
        {
            if (await SetClipboard([this]))
            {
                Exclude.Execute();
            }
        }
        else
        {
            if (await SetClipboard([.. targets]))
            {
                var history = Timeline.EditorContext.GetRequiredService<HistoryManager>();
                foreach (ElementViewModel target in targets)
                {
                    Scene.RemoveChild(target.Model);
                }

                history.Commit(CommandNames.CutElement);
            }
        }
    }

    private void OnSplit(TimeSpan timeSpan)
    {
        IReadOnlyList<ElementViewModel> targets = GetGroupOrSelectedElements();
        if (targets.Count == 0)
        {
            targets = [this];
        }

        int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
        TimeSpan minLength = TimeSpan.FromSeconds(1d / rate);
        TimeSpan absTime = timeSpan.RoundToRate(rate);

        var groupUpdates = new Dictionary<int, List<Guid>>();

        foreach (ElementViewModel target in targets)
        {
            TimeSpan forwardLength = absTime - target.Model.Start;
            TimeSpan backwardLength = target.Model.Length - forwardLength;

            if (forwardLength < minLength || backwardLength < minLength)
                continue;

            ObjectRegenerator.Regenerate(target.Model, out Element backward);

            target.Scene.MoveChild(
                target.Model.ZIndex,
                target.Model.Start,
                forwardLength,
                target.Model);
            backward.Start = absTime;
            backward.Length = backwardLength;
            foreach (KeyFrameAnimation item in new ObjectSearcher(backward,
                             o => o is KeyFrameAnimation { UseGlobalClock: false })
                         .SearchAll()
                         .OfType<KeyFrameAnimation>())
            {
                foreach (IKeyFrame keyframe in item.KeyFrames)
                {
                    keyframe.KeyTime -= forwardLength;
                }
            }

            CoreSerializer.StoreToUri(
                backward,
                RandomFileNameGenerator.GenerateUri(Scene.Uri!, Constants.ElementFileExtension));
            target.Scene.AddChild(backward);
            backward.Operation.OnSplit(true, forwardLength, -forwardLength);
            target.Model.Operation.OnSplit(false, TimeSpan.Zero, -backwardLength);

            if (target._elementGroup is { } set)
            {
                int index = target.Scene.Groups.IndexOf(set);
                if (index >= 0)
                {
                    if (!groupUpdates.TryGetValue(index, out List<Guid>? newIds))
                    {
                        newIds = [];
                        groupUpdates.Add(index, newIds);
                    }

                    newIds.Add(backward.Id);
                }
            }
        }

        foreach ((int index, List<Guid> value) in groupUpdates.OrderByDescending(x => x.Key))
        {
            ImmutableHashSet<Guid> newGroup = value.ToImmutableHashSet();
            if (newGroup.Count >= 2)
            {
                var scene = Scene;
                scene.Groups.Insert(index + 1, newGroup);
            }
        }

        Timeline.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.SplitElement);
    }

    private async void OnChangeToOriginalLength()
    {
        if (!Model.UseNode
            && Model.Operation.Children.FirstOrDefault(v => v.HasOriginalLength()) is { } op
            && op.TryGetOriginalLength(out TimeSpan timeSpan))
        {
            PrepareAnimationContext context = PrepareAnimation();

            int rate = Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;
            TimeSpan length = timeSpan.FloorToRate(rate);

            Element? after = Model.GetAfter(Model.ZIndex, Model.Range.End);
            if (after != null)
            {
                TimeSpan delta = after.Start - Model.Start;
                if (delta < length)
                {
                    length = delta;
                }
            }

            Scene.MoveChild(Model.ZIndex, Model.Start, length, Model);
            Timeline.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.MoveElement);

            await AnimationRequest(context);
        }
    }

    public bool HasOriginalLength()
    {
        return !Model.UseNode && Model.Operation.Children.Any(v => v.HasOriginalLength());
    }

    public void Execute(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;
        switch (execution.CommandName)
        {
            case "Rename":
                RenameRequested();
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
            ThumbnailsKind.Value = ElementThumbnailsKind.None;
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

            if (provider != null)
            {
                _thumbnailsInvalidatedHandler = (_, _) => _thumbnailsInvalidatedSubject.OnNext(Unit.Default);
                provider.ThumbnailsInvalidated += _thumbnailsInvalidatedHandler;
            }
        }

        if (provider == null)
        {
            ThumbnailsKind.Value = ElementThumbnailsKind.None;
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
                case ElementThumbnailsKind.Video:
                    await UpdateVideoThumbnailsAsync(provider, ct);
                    break;
                case ElementThumbnailsKind.Audio:
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
    }

    private IElementThumbnailsProvider? FindThumbnailsProvider()
    {
        if (Model.UseNode)
            return null;

        foreach (var child in Model.Operation.Children)
        {
            if (child is IElementThumbnailsProvider provider)
                return provider;
        }

        return null;
    }

    private async Task UpdateVideoThumbnailsAsync(IElementThumbnailsProvider provider, CancellationToken ct)
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

        await foreach (var (index, count, thumbnail) in provider.GetThumbnailStripAsync((int)width, MaxThumbnailHeight, ct))
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
                        ThumbnailReady?.Invoke(index, ConvertToAvaloniaBitmap(thumbnail));
                    }
                }
            });
        }
    }

    private async Task UpdateAudioThumbnailsAsync(IElementThumbnailsProvider provider, CancellationToken ct)
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

        await foreach (var chunk in provider.GetWaveformChunksAsync(chunkCount, MaxSamplesPerChunk, ct))
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

    private static Bitmap? ConvertToAvaloniaBitmap(IBitmap source)
    {
        if (source.IsDisposed)
            return null;

        var width = source.Width;
        var height = source.Height;

        return new Bitmap(
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul,
            source.Data,
            new Avalonia.PixelSize(width, height),
            new Vector(96, 96),
            width * 4);
    }
}
