using System.Collections.Specialized;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Utilities;
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
    private DispatcherTimer? _nudgeCommitTimer;
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

        // Undo/Redo の直前で debounce 中の Nudge を必ず Commit する。
        // 未コミットのままだと、Undo が先に未確定 transaction を revert してから
        // 前回コミットを Pop するため 2 アクション分が同時に取り消されてしまう。
        editorContext.GetRequiredService<HistoryManager>()
            .BeforeMutation
            .Subscribe(_ => FlushPendingNudgeCommit())
            .AddTo(_disposables);

        _logger.LogInformation("TimelineTabViewModel initialized successfully.");
    }

    private void RaiseCanExecuteChanged()
    {
        if (!_isDisposed)
        {
            _canExecuteChangedSubject.OnNext(System.Reactive.Unit.Default);
        }
    }

    private void SetStartTimeCore(TimeSpan time)
    {
        _logger.LogInformation("Adjusting scene start to pointer position.");
        if (time > Scene.Duration + Scene.Start)
        {
            int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
            var endTime = Scene.Duration + Scene.Start;
            time -= endTime;
            time += TimeSpan.FromSeconds(1d / rate);

            Scene.Duration = time;
            Scene.Start = endTime;
        }
        else
        {
            int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }
            else if (time > Scene.Duration + Scene.Start)
            {
                time = Scene.Duration + Scene.Start - TimeSpan.FromSeconds(1d / rate);
            }

            Scene.Duration = TimeSpan.FromTicks(Math.Max(
                (Scene.Duration + Scene.Start -
                 time).Ticks, TimeSpan.FromSeconds(1d / rate).Ticks));
            Scene.Start = time;
        }

        EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.ChangeSceneStart);

        _logger.LogInformation("Scene start adjusted to {Time}.", time);
    }

    private void OnSetStartTimeToPointerPosition()
    {
        TimeSpan time = ClickedFrame;
        SetStartTimeCore(time);
    }

    private void SetEndTimeCore(TimeSpan time)
    {
        _logger.LogInformation("Adjusting scene duration to pointer position.");
        if (time < Scene.Start)
        {
            int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
            time -= TimeSpan.FromSeconds(1d / rate);
            if (time < TimeSpan.Zero)
            {
                time = TimeSpan.Zero;
            }

            Scene.Duration = Scene.Start - time;
            Scene.Start = time;
            EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.ChangeSceneDuration);
        }
        else
        {
            int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
            time -= Scene.Start;
            if (time <= TimeSpan.Zero)
            {
                time = TimeSpan.FromSeconds(1d / rate);
            }

            Scene.Duration = time;
            EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.ChangeSceneDuration);
            _logger.LogInformation("Scene duration adjusted to {Time}.", time);
        }
    }

    private void OnSetEndTimeToPointerPosition()
    {
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = ClickedFrame + TimeSpan.FromSeconds(1d / rate);
        SetEndTimeCore(time);
    }

    private void OnSetStartTimeToCurrentTime()
    {
        TimeSpan time = CurrentTime.Value;
        SetStartTimeCore(time);
    }

    private void OnSetEndTimeToCurrentTime()
    {
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = CurrentTime.Value + TimeSpan.FromSeconds(1d / rate);
        SetEndTimeCore(time);
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

    private (TimeSpan, int) CorrectPosition(TimeRange range, int minZIndex, int maxZIndex)
    {
        // クリック位置の近傍を「時計回りの渦巻き」で探索し、重ならない最初の位置に移動
        var fps = Scene.FindHierarchicalParent<Project>()!.GetFrameRate();
        var length = range.Duration;
        var layerCount = maxZIndex - minZIndex + 1;
        var step = TimeSpan.FromSeconds(1d / fps);

        TimeSpan newStart = ClickedFrame;
        int newZIndex = CalculateClickedLayer();

        // 時計回り: 右->下->左->上
        int[] dx = [1, 0, -1, 0]; // 時間方向
        int[] dz = [0, 1, 0, -1]; // レイヤー方向

        // まずはクリック位置を試す
        if (!IsOverlapping(new TimeRange(newStart, length), newZIndex, newZIndex + layerCount - 1))
        {
            return (newStart, newZIndex);
        }

        int dir = 0; // 進行方向のインデックス
        int stepLen = 1; // 現在の方向に進む歩数
        int stepped = 0; // その方向で進んだ歩数
        int turnCount = 0; // 方向転換回数（2回ごとに stepLen を+1）

        // タイムラインが要素で隙間なく埋め尽くされた病的なケースで UI スレッドが
        // 永久ループに陥らないよう、探索回数に上限を設ける。100_000 ステップは
        // 30fps なら ±1500 フレーム ≒ ±50 秒分の渦巻きをカバーするので、
        // 実運用で到達することは想定していない。到達した場合は最後に検査した
        // 候補位置をそのまま返す (Overlap している可能性があるが、UI フリーズより
        // 既存要素と重なる方が回復可能)。
        const int MaxSearchSteps = 100_000;
        int searchSteps = 0;
        bool capped = false;

        while (true)
        {
            // 1歩進ませる
            long dtTicks = step.Ticks * dx[dir];
            newStart += TimeSpan.FromTicks(dtTicks);
            newZIndex += dz[dir];

            // 必要なら境界処理（例：負の時間を禁止）
            if (newStart < TimeSpan.Zero) newStart = TimeSpan.Zero;
            if (newZIndex < 0) newZIndex = 0;

            // 空きが見つかったら終了
            if (!IsOverlapping(new TimeRange(newStart, length), newZIndex, newZIndex + layerCount - 1))
                break;

            if (++searchSteps >= MaxSearchSteps)
            {
                capped = true;
                break;
            }

            // 歩数管理：指定歩数進んだら時計回りに方向転換
            stepped++;
            if (stepped == stepLen)
            {
                stepped = 0;
                dir = (dir + 1) & 3; // 0..3 の循環
                turnCount++;
                if ((turnCount & 1) == 0)
                    stepLen++; // 2回方向転換するごとに 1,1,2,2,3,3,… と広がる
            }
        }

        if (capped)
        {
            _logger.LogWarning(
                "CorrectPosition gave up after {Steps} steps without finding a non-overlapping slot; using last candidate at start={Start}, zIndex={ZIndex}.",
                searchSteps, newStart, newZIndex);
        }

        return (newStart, newZIndex);

        // TimeRangeとレイヤーの範囲(max, min)を与えると、重なるかどうかを調べる関数
        bool IsOverlapping(TimeRange range, int minZIndex, int maxZIndex)
        {
            return Elements.Any(e =>
                (e.Model.Range == range || e.Model.Range.Intersects(range) ||
                 e.Model.Range.Contains(range) || range.Contains(e.Model.Range))
                && e.Model.ZIndex >= minZIndex && e.Model.ZIndex <= maxZIndex);
        }
    }

    private async Task PasteElementList(IClipboard clipboard)
    {
        if (await clipboard.TryGetValueAsync(BeutlDataFormats.Elements) is not { } json
            || JsonNode.Parse(json) is not JsonArray jsonArray) return;

        var oldElements = jsonArray
            .Select(node => (node, element: new Element()))
            .Do(t => CoreSerializer.PopulateFromJsonObject(t.element, t.node!.AsObject()))
            .Select(t => t.element)
            .ToArray();

        ObjectRegenerator.Regenerate(oldElements, out Element[] newElements);

        TimeSpan minStart = newElements.Min(e => e.Start);
        int minZIndex = newElements.Min(e => e.ZIndex);
        TimeSpan maxStart = newElements.Max(e => e.Start);
        int maxZIndex = newElements.Max(e => e.ZIndex);
        TimeSpan length = maxStart - minStart;

        var (newStart, newZIndex) = CorrectPosition(
            new TimeRange(minStart, length),
            minZIndex,
            maxZIndex);

        DuplicateHelper.PlaceDuplicates(
            Scene, newElements, oldElements, newStart, newZIndex, Constants.ElementFileExtension);
        EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.PasteElement);

        ScrollTo.Execute((new TimeRange(newStart, length), newZIndex));
    }

    private void DuplicateSelectedElements()
    {
        try
        {
            if (SelectedElements.Count == 0) return;

            HashSet<Guid> ids = DuplicateHelper.ExpandWithGroupSiblings(
                SelectedElements.Select(s => s.Model.Id),
                Scene.Groups);

            var sourceVMs = Elements.Where(x => ids.Contains(x.Model.Id)).ToArray();
            if (sourceVMs.Length == 0)
            {
                // SelectedElements は非空だが Elements の側で ID が解決できない
                // = 選択モデルと VM コレクションのライフサイクルが desync している。
                // どちらかの管理にバグがあるので警告として残す (ユーザー操作では起きない想定)。
                _logger.LogWarning(
                    "Duplicate skipped: selected element IDs did not resolve to Elements. Ids={Ids}",
                    string.Join(", ", ids));
                return;
            }

            var oldElements = sourceVMs.Select(x => x.Model).ToArray();
            ObjectRegenerator.Regenerate(oldElements, out Element[] newElements);

            (TimeRange seedRange, int minZIndex, int maxZIndex) = DuplicateHelper.ComputePlacementRange(newElements);
            var (newStart, newZIndex) = CorrectPosition(seedRange, minZIndex, maxZIndex);

            DuplicateHelper.PlaceDuplicates(
                Scene, newElements, oldElements, newStart, newZIndex, Constants.ElementFileExtension);
            EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.DuplicateElement);

            ScrollTo.Execute((new TimeRange(newStart, seedRange.Duration), newZIndex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while duplicating elements.");
            NotificationService.ShowError(MessageStrings.UnexpectedError, ex.Message);
        }
    }

    internal void DuplicateElementsAt(IReadOnlyList<Element> sourceElements, TimeSpan anchorStart, int anchorZIndex)
    {
        if (sourceElements.Count == 0)
        {
            // Alt+drag は閾値未満のドラッグでここに来うる。通常経路なので
            // notification は出さないが、想定外の no-op を後追いできるよう debug ログを残す。
            _logger.LogDebug("DuplicateElementsAt skipped: sourceElements is empty.");
            return;
        }

        try
        {
            var src = sourceElements.ToArray();
            ObjectRegenerator.Regenerate(src, out Element[] newElements);

            DuplicateHelper.PlaceDuplicates(
                Scene, newElements, src, anchorStart, Math.Max(anchorZIndex, 0), Constants.ElementFileExtension);
            EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.DuplicateElement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while duplicating elements at position.");
            NotificationService.ShowError(MessageStrings.UnexpectedError, ex.Message);
        }
    }

    private async Task PasteElement(IClipboard clipboard)
    {
        if (await clipboard.TryGetValueAsync(BeutlDataFormats.Element) is not { } json) return;
        if (JsonNode.Parse(json) is not JsonObject jsonObject) return;

        var oldElement = new Element();

        CoreSerializer.PopulateFromJsonObject(oldElement, jsonObject);

        ObjectRegenerator.Regenerate(oldElement, out Element newElement);

        newElement.Start = ClickedFrame;
        newElement.ZIndex = CalculateClickedLayer();

        CoreSerializer.StoreToUri(newElement, RandomFileNameGenerator.GenerateUri(
            Scene.Uri!, Constants.ElementFileExtension));

        HistoryManager history = EditorContext.GetRequiredService<HistoryManager>();
        Scene.AddChild(newElement);
        history.Commit(CommandNames.PasteElement);

        ScrollTo.Execute((newElement.Range, newElement.ZIndex));
    }

    private async Task PasteImageElement(IClipboard clipboard)
    {
        var imageData = await clipboard.TryGetBitmapAsync();
        if (imageData == null) return;

        string dir = Path.GetDirectoryName(Scene.Uri!.LocalPath)!;
        // 画像を保存
        string resDir = Path.Combine(dir, "resources");
        if (!Directory.Exists(resDir))
        {
            Directory.CreateDirectory(resDir);
        }

        string imageFile = RandomFileNameGenerator.Generate(resDir, "png");
        imageData.Save(imageFile);

        var sourceImage = new Graphics.SourceImage();
        sourceImage.Source.CurrentValue = ImageSource.Open(imageFile);
        var newElement = new Element
        {
            Start = ClickedFrame,
            Length = TimeSpan.FromSeconds(5),
            ZIndex = CalculateClickedLayer(),
            AccentColor = ColorGenerator.GenerateColor(typeof(Graphics.SourceImage).FullName!),
            Name = Path.GetFileName(imageFile)
        };
        newElement.AddObject(sourceImage);

        CoreSerializer.StoreToUri(newElement, RandomFileNameGenerator.GenerateUri(
            dir, Constants.ElementFileExtension));

        HistoryManager history = EditorContext.GetRequiredService<HistoryManager>();
        Scene.AddChild(newElement);
        history.Commit(CommandNames.PasteElement);

        ScrollTo.Execute((newElement.Range, newElement.ZIndex));
    }

    private async Task PasteFiles(IClipboard clipboard)
    {
        if (await clipboard.TryGetFilesAsync() is not { } files) return;

        var frame = ClickedFrame;
        int layer = CalculateClickedLayer();
        foreach (IStorageItem item in files)
        {
            if (item.TryGetLocalPath() is not { } fileName) continue;

            AddElement.Execute(new ElementDescription(
                frame, TimeSpan.FromSeconds(5), layer,
                FileName: fileName));
        }
    }

    private async void PasteCore()
    {
        try
        {
            IClipboard? clipboard = ClipboardHelper.GetClipboard();
            if (clipboard == null) return;

            var formats = await clipboard.GetDataFormatsAsync();

            if (formats.Contains(BeutlDataFormats.Elements))
            {
                await PasteElementList(clipboard);
            }
            else if (formats.Contains(BeutlDataFormats.Element))
            {
                await PasteElement(clipboard);
            }
            else if (formats.Contains(DataFormat.File))
            {
                await PasteFiles(clipboard);
            }
            else if (formats.Contains(DataFormat.Bitmap))
            {
                await PasteImageElement(clipboard);
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
        // SelectedElements は HashSet で順序不定。RoundToRate を適用する anchor を
        // 決定論的にするため Start (副: ZIndex) でソートして最も左の要素を選ぶ。
        // 異なる off-grid Start を持つ要素間で実行毎にシフト量が変わるのを防ぐ。
        ElementViewModel? first = SelectedElements
            .OrderBy(e => e.Model.Start)
            .ThenBy(e => e.Model.ZIndex)
            .FirstOrDefault();
        if (first is null) return;

        // 他の編集操作との一貫性と、グループの位置関係が崩れることを防ぐため、
        // メンバーが 1 つだけ選択されていてもグループ全体を移動対象にする。
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

        if (frames == 0) return;

        // ドラッグ移動と同じく、結果がフレームグリッドに乗るようアンカー要素
        // (= 最初の選択) の現在 Start を一旦 RoundToRate で丸めてから N フレーム分シフトする。
        // TimeSpan.FromSeconds は repeating fraction を tick へ丸めるため、
        // 連打で sub-frame ドリフトする可能性があるので int.ToTimeSpan(rate) で
        // 整数 tick 計算にする。
        Element anchor = first.Model;
        TimeSpan anchoredStart = anchor.Start.RoundToRate(rate) + frames.ToTimeSpan(rate);
        if (anchoredStart < TimeSpan.Zero) return;

        TimeSpan delta = anchoredStart - anchor.Start;
        if (delta == TimeSpan.Zero) return;

        Scene.MoveChildren(0, delta, targets.Select(x => x.Model).ToArray());
        ScheduleNudgeCommit();
    }

    // 連続押下を 1 つの Undo にまとめるための debounce。
    // 典型的なキーリピート間隔 (~30-50ms) より十分長く、かつ単発の意図的な
    // ナッジとリピート押下のひとかたまりを分離できる値として 300ms を採用 (経験則)。
    //
    // 既知の制約: debounce 中 (~300ms 以内) に Timeline 外の経路 (例:
    // ElementViewModel の色変更が直接 Commit する) が走ると、その操作の Record と
    // 未コミット nudge ops が同じ HistoryTransaction にマージしてしまう。
    // 完全に分離するには nudge 専用 transaction の導入が必要だが、UI 上 300ms 以内
    // にダイアログを伴う操作が完結することは稀なため、現状はこの制約を受け入れる。
    private void ScheduleNudgeCommit()
    {
        if (_nudgeCommitTimer is null)
        {
            _nudgeCommitTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(300),
                DispatcherPriority.Background,
                OnNudgeCommitTick);
        }

        _nudgeCommitTimer.Stop();
        _nudgeCommitTimer.Start();
    }

    private void OnNudgeCommitTick(object? sender, EventArgs e)
    {
        _nudgeCommitTimer?.Stop();
        if (_isDisposed) return;
        // Shutdown では HistoryManager が VM より先に Dispose されることがあり、
        // その場合 Commit が ObjectDisposedException を投げる。Tick はバックグラウンド
        // から UI スレッドに上がってくる経路なので、未捕捉例外を回避するため
        // Dispose 経路と同様に防御する。
        try
        {
            EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.MoveElement);
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogWarning(ex, "Pending nudge commit dropped: HistoryManager already disposed.");
        }
    }

    private void FlushPendingNudgeCommit()
    {
        // Timer が止まっている = コミット待ちの Nudge は無い (Tick 実行済み、既に Flush 済み、
        // または未スタート)。同じ debounce ウィンドウを Undo / 他コマンド実行 / Dispose の
        // 各経路から重ねて Flush しても二重コミットにならないようガードする。
        if (_nudgeCommitTimer is null || !_nudgeCommitTimer.IsEnabled) return;
        _nudgeCommitTimer.Stop();
        EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.MoveElement);
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
