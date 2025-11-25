using System.Collections.Specialized;
using System.Numerics;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.Operation;
using Beutl.Operators.Source;
using Beutl.ProjectSystem;
using Beutl.Reactive;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using static Beutl.ViewModels.BufferStatusViewModel;

namespace Beutl.ViewModels;

public interface ITimelineOptionsProvider
{
    Scene Scene { get; }

    IReactiveProperty<TimelineOptions> Options { get; }

    IObservable<float> Scale { get; }

    IObservable<Vector2> Offset { get; }
}

public sealed class TimelineViewModel : IToolContext, IContextCommandHandler
{
    private readonly ILogger _logger = Log.CreateLogger<TimelineViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly Subject<LayerHeaderViewModel> _layerHeightChanged = new();
    private readonly Dictionary<int, TrackedLayerTopObservable> _trackerCache = [];

    public TimelineViewModel(EditViewModel editViewModel)
    {
        _logger.LogInformation("Initializing TimelineViewModel.");
        EditorContext = editViewModel;
        Scene = editViewModel.Scene;
        Player = editViewModel.Player;
        FrameSelectionRange = new FrameSelectionRange(editViewModel.Scale).DisposeWith(_disposables);

        SeekBarMargin = editViewModel.CurrentTime
            .CombineLatest(editViewModel.Scale)
            .Select(item => new Thickness(Math.Max(item.First.ToPixel(item.Second), 0), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        StartingBarMargin = Scene.GetObservable(Scene.StartProperty)
            .CombineLatest(editViewModel.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        EndingBarMargin = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(editViewModel.Scale, StartingBarMargin)
            .Select(item => item.First.ToPixel(item.Second) + item.Third.Left)
            .Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

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
            .AddTo(_disposables);

        AddElement.Subscribe(editViewModel.AddElement).AddTo(_disposables);

        Paste.Subscribe(PasteCore)
            .AddTo(_disposables);

        TimelineOptions options = editViewModel.Options.Value;
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
                    this.GetService<ISupportCloseAnimation>()?.Close(element.Model);
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

        editViewModel.Options.Select(x => x.MaxLayerCount)
            .DistinctUntilChanged()
            .Subscribe(TryApplyLayerCount);

        SetStartTimeToPointerPosition.Subscribe(OnSetStartTimeToPointerPosition);
        SetEndTimeToPointerPosition.Subscribe(OnSetEndTimeToPointerPosition);
        SetStartTimeToCurrentTime.Subscribe(OnSetStartTimeToCurrentTime);
        SetEndTimeToCurrentTime.Subscribe(OnSetEndTimeToCurrentTime);
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;

        AutoAdjustSceneDuration = editorConfig.GetObservable(EditorConfig.AutoAdjustSceneDurationProperty)
            .ToReactiveProperty();
        AutoAdjustSceneDuration.Subscribe(b =>
        {
            _logger.LogDebug("AutoAdjustSceneDuration changed to {Value}.", b);
            editorConfig.AutoAdjustSceneDuration = b;
        });

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
                EditorContext.FrameCacheManager.Value.Clear();
            });

        DeleteFrameCache = HoveredCacheBlock.Select(v => v != null)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is not { } block) return;

                _logger.LogInformation("Deleting frame cache for block starting at frame {StartFrame}.",
                    block.StartFrame);
                FrameCacheManager manager = EditorContext.FrameCacheManager.Value;
                if (block.IsLocked)
                {
                    manager.Unlock(
                        block.StartFrame, block.StartFrame + block.LengthFrame);
                }

                manager.DeleteAndUpdateBlocks(
                    [(block.StartFrame, block.StartFrame + block.LengthFrame)]);
            });

        LockFrameCache = HoveredCacheBlock.Select(v => v?.IsLocked == false)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is not { } block) return;

                _logger.LogInformation("Locking frame cache for block starting at frame {StartFrame}.",
                    block.StartFrame);
                FrameCacheManager manager = EditorContext.FrameCacheManager.Value;
                manager.Lock(
                    block.StartFrame, block.StartFrame + block.LengthFrame);

                manager.UpdateBlocks();
            });

        UnlockFrameCache = HoveredCacheBlock.Select(v => v?.IsLocked == true)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is not { } block) return;

                _logger.LogInformation("Unlocking frame cache for block starting at frame {StartFrame}.",
                    block.StartFrame);
                FrameCacheManager manager = EditorContext.FrameCacheManager.Value;
                manager.Unlock(
                    block.StartFrame, block.StartFrame + block.LengthFrame);

                manager.UpdateBlocks();
            });

        _logger.LogInformation("TimelineViewModel initialized successfully.");
    }

    private void SetStartTimeCore(TimeSpan time)
    {
        _logger.LogInformation("Adjusting scene start to pointer position.");
        if (time > Scene.Duration + Scene.Start)
        {
            int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
            time -= Scene.Duration + Scene.Start;
            time += TimeSpan.FromSeconds(1d / rate);

            RecordableCommands.Edit(Scene, Scene.DurationProperty, time)
                .Append(RecordableCommands.Edit(Scene, Scene.StartProperty, Scene.Duration + Scene.Start))
                .WithStoables([Scene])
                .DoAndRecord(EditorContext.CommandRecorder);
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

            RecordableCommands.Edit(Scene, Scene.StartProperty, time)
                .Append(RecordableCommands.Edit(Scene, Scene.DurationProperty, TimeSpan.FromTicks(Math.Max(
                    (Scene.Duration + Scene.Start -
                     time).Ticks, TimeSpan.FromSeconds(1d / rate).Ticks))))
                .WithStoables([Scene])
                .DoAndRecord(EditorContext.CommandRecorder);
        }

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

            RecordableCommands.Edit(Scene, Scene.StartProperty, time)
                .Append(RecordableCommands.Edit(Scene, Scene.DurationProperty, Scene.Start - time))
                .WithStoables([Scene])
                .DoAndRecord(EditorContext.CommandRecorder);
        }
        else
        {
            int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
            time -= Scene.Start;
            if (time <= TimeSpan.Zero)
            {
                time = TimeSpan.FromSeconds(1d / rate);
            }

            RecordableCommands.Edit(Scene, Scene.DurationProperty, time)
                .WithStoables([Scene])
                .DoAndRecord(EditorContext.CommandRecorder);
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
        TimeSpan time = EditorContext.CurrentTime.Value;
        SetStartTimeCore(time);
    }

    private void OnSetEndTimeToCurrentTime()
    {
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = EditorContext.CurrentTime.Value + TimeSpan.FromSeconds(1d / rate);
        SetEndTimeCore(time);
    }

    public Scene Scene { get; private set; }

    public PlayerViewModel Player { get; private set; }

    public EditViewModel EditorContext { get; private set; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> StartingBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReactiveCommand<ElementDescription> AddElement { get; } = new();

    public CoreList<ElementViewModel> Elements { get; } = [];

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = [];

    public CoreList<LayerHeaderViewModel> LayerHeaders { get; } = [];

    public ReactiveCommand Paste { get; } = new();

    public ReactiveCommand<(TimeRange Range, int ZIndex)> ScrollTo { get; } = new();

    public ReactiveCommandSlim SetStartTimeToPointerPosition { get; } = new();

    public ReactiveCommandSlim SetEndTimeToPointerPosition { get; } = new();

    public ReactiveCommandSlim SetStartTimeToCurrentTime { get; } = new();

    public ReactiveCommandSlim SetEndTimeToCurrentTime { get; } = new();

    public ReactiveCommandSlim DeleteAllFrameCache { get; }

    public ReactiveCommandSlim DeleteFrameCache { get; }

    public ReactiveCommandSlim LockFrameCache { get; }

    public ReactiveCommandSlim UnlockFrameCache { get; }

    public ReactiveProperty<bool> AutoAdjustSceneDuration { get; }

    public ReactivePropertySlim<CacheBlock?> HoveredCacheBlock { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsLockCacheButtonEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> IsUnlockCacheButtonEnabled { get; }

    public FrameSelectionRange FrameSelectionRange { get; }

    public TimeSpan ClickedFrame { get; set; }

    public Point ClickedPosition { get; set; }

    public HashSet<ElementViewModel> SelectedElements { get; } = [];

    public IReactiveProperty<TimelineOptions> Options => EditorContext.Options;

    public ToolTabExtension Extension => TimelineTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.LeftLowerBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public IObservable<LayerHeaderViewModel> LayerHeightChanged => _layerHeightChanged;

    public string Header => Strings.Timeline;

    public void Dispose()
    {
        _logger.LogInformation("Disposing TimelineViewModel.");
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

        Inlines.Clear();
        LayerHeaders.Clear();
        Elements.Clear();
        Scene = null!;
        Player = null!;
        EditorContext = null!;
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
        string? json = await clipboard.TryGetValueAsync(BeutlDataFormats.Elements);
        if (json == null || JsonNode.Parse(json) is not JsonArray jsonArray) return;

        var oldElements = jsonArray
            .Select(node => (node, element: new Element()))
            .Do(t => CoreSerializerHelper.PopulateFromJsonObject(t.element, t.node!.AsObject()))
            .Select(t => t.element)
            .ToArray();

        ObjectRegenerator.Regenerate(oldElements, out Element[] newElements);

        // 時間の範囲、レイヤーの範囲を計算
        TimeSpan minStart = newElements.Min(e => e.Start);
        int minZIndex = newElements.Min(e => e.ZIndex);
        TimeSpan maxStart = newElements.Max(e => e.Start);
        int maxZIndex = newElements.Max(e => e.ZIndex);
        TimeSpan length = maxStart - minStart;

        var (newStart, newZIndex) = CorrectPosition(
            new TimeRange(minStart, length),
            minZIndex,
            maxZIndex);

        // 新しい位置に移動して保存、シーンに追加
        foreach (Element newElement in newElements)
        {
            newElement.Start = newElement.Start - minStart + newStart;
            newElement.ZIndex = newElement.ZIndex - minZIndex + newZIndex;

            newElement.Save(RandomFileNameGenerator.Generate(
                Path.GetDirectoryName(Scene.FileName)!,
                Constants.ElementFileExtension));
        }

        CommandRecorder recorder = EditorContext.CommandRecorder;
        newElements.Select(i => Scene.AddChild(i))
            .ToArray()
            .ToCommand()
            .DoAndRecord(recorder);
        ScrollTo.Execute((new TimeRange(newStart, maxStart - minStart), newZIndex));
    }

    private async Task PasteElement(IClipboard clipboard)
    {
        if (await clipboard.TryGetValueAsync(BeutlDataFormats.Element) is not { } json) return;
        if (JsonNode.Parse(json) is not JsonObject jsonObject) return;

        var oldElement = new Element();

        CoreSerializerHelper.PopulateFromJsonObject(oldElement, jsonObject);

        ObjectRegenerator.Regenerate(oldElement, out Element newElement);

        newElement.Start = ClickedFrame;
        newElement.ZIndex = CalculateClickedLayer();

        newElement.Save(RandomFileNameGenerator.Generate(Path.GetDirectoryName(Scene.FileName)!,
            Constants.ElementFileExtension));

        CommandRecorder recorder = EditorContext.CommandRecorder;
        Scene.AddChild(newElement).DoAndRecord(recorder);

        ScrollTo.Execute((newElement.Range, newElement.ZIndex));
    }

    private async Task PasteImageElement(IClipboard clipboard)
    {
        var imageData = await clipboard.TryGetBitmapAsync();
        if (imageData == null) return;

        string dir = Path.GetDirectoryName(Scene.FileName)!;
        // 画像を保存
        string resDir = Path.Combine(dir, "resources");
        if (!Directory.Exists(resDir))
        {
            Directory.CreateDirectory(resDir);
        }

        string imageFile = RandomFileNameGenerator.Generate(resDir, "png");
        imageData.Save(imageFile);

        var sp = new SourceImageOperator();
        sp.Value.Source = BitmapSource.Open(imageFile);
        var newElement = new Element
        {
            Start = ClickedFrame,
            Length = TimeSpan.FromSeconds(5),
            ZIndex = CalculateClickedLayer(),
            Operation = { Children = { sp } },
            AccentColor = ColorGenerator.GenerateColor(typeof(SourceImageOperator).FullName!),
            Name = Path.GetFileName(imageFile)
        };

        newElement.Save(RandomFileNameGenerator.Generate(dir, Constants.ElementFileExtension));

        CommandRecorder recorder = EditorContext.CommandRecorder;
        Scene.AddChild(newElement).DoAndRecord(recorder);

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
            IClipboard? clipboard = App.GetClipboard();
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
            NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred, ex.Message);
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
            Animatable? animatable = null;

            void FindAndSetAncestor(Span<object> span, KeyFrameAnimation kfAnm)
            {
                for (int i = 0; i < span.Length; i++)
                {
                    switch (span[i])
                    {
                        case IAnimatablePropertyAdapter anmProp2 when ReferenceEquals(anmProp2.Animation, kfAnm):
                            anmProp = anmProp2;
                            return;
                        case Animatable animatable2:
                            animatable = animatable2;
                            return;
                    }
                }
            }

            bool Predicate(Stack<object> stack, object obj)
            {
                if (obj is KeyFrameAnimation kfAnm && kfAnm.Id == anmId)
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

            if (searcher.Search() is KeyFrameAnimation anm)
            {
                if (anmProp != null)
                {
                    AttachInline(anmProp, element);
                    _logger.LogDebug("Inline animation attached for element {ElementId} and animation {AnimationId}.",
                        element.Id, anmId);
                }
                else if (animatable != null)
                {
                    try
                    {
                        Type type = typeof(AnimatablePropertyAdapter<>).MakeGenericType(anm.Property.PropertyType);
                        var createdProp =
                            (IAnimatablePropertyAdapter)Activator.CreateInstance(type, anm.Property, animatable)!;
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

        var array = new JsonArray();
        array.AddRange(LayerHeaders.Where(v => v.ShouldSaveState())
            .Select(v =>
            {
                var obj = new JsonObject { [nameof(LayerHeaderViewModel.Number)] = v.Number.Value };
                v.WriteToJson(obj);
                _logger.LogDebug("LayerHeader {Number} state written to JSON.", v.Number.Value);
                return obj;
            }));

        json[nameof(LayerHeaders)] = array;

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
    }

    public void SelectElement(ElementViewModel item)
    {
        SelectedElements.Add(item);
        item.IsSelected.Value = true;
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

    public void Execute(ContextCommandExecution execution)
    {
        _logger.LogDebug("Executing context command {CommandName}.", execution.CommandName);
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
        }
    }

    private sealed class TrackedLayerTopObservable(int layerNum, TimelineViewModel timeline)
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
