using System.Buffers;
using System.Collections.Specialized;
using System.Numerics;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using System.Windows.Input;

using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;

using Beutl.Animation;
using Beutl.Commands;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Models;
using Beutl.Operators.Configure;
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

public sealed class TimelineViewModel : IToolContext
{
    private readonly ILogger _logger = Log.CreateLogger<TimelineViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly Subject<LayerHeaderViewModel> _layerHeightChanged = new();
    private readonly Dictionary<int, TrackedLayerTopObservable> _trackerCache = [];

    public TimelineViewModel(EditViewModel editViewModel)
    {
        EditorContext = editViewModel;
        Scene = editViewModel.Scene;
        Player = editViewModel.Player;
        FrameSelectionRange = new FrameSelectionRange(editViewModel.Scale).DisposeWith(_disposables);
        PanelWidth = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(editViewModel.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        SeekBarMargin = editViewModel.CurrentTime
            .CombineLatest(editViewModel.Scale)
            .Select(item => new Thickness(Math.Max(item.First.ToPixel(item.Second), 0), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        EndingBarMargin = PanelWidth.Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        AddElement.Subscribe(editViewModel.AddElement).AddTo(_disposables);

        TimelineOptions options = editViewModel.Options.Value;
        LayerHeaders.AddRange(Enumerable.Range(0, options.MaxLayerCount).Select(num => new LayerHeaderViewModel(num, this)));
        if (Scene.Children.Count > 0)
        {
            AddLayerHeaders(Scene.Children.Max(i => i.ZIndex) + 1);
            var items = new ElementViewModel[Scene.Children.Count];
            Parallel.ForEach(
                Scene.Children,
                (item, _, idx) => items[idx] = new ElementViewModel(item, this));
            Elements.AddRange(items);
        }

        Scene.Children.TrackCollectionChanged(
            (idx, item) =>
            {
                AddLayerHeaders(item.ZIndex + 1);
                Elements.Insert(idx, new ElementViewModel(item, this));
            },
            (idx, _) =>
            {
                ElementViewModel element = Elements[idx];
                this.GetService<ISupportCloseAnimation>()?.Close(element.Model);
                Elements.RemoveAt(idx);
                element.Dispose();
            },
            () =>
            {
                ElementViewModel[] tmp = [.. Elements];
                Elements.Clear();
                foreach (ElementViewModel? item in Elements.GetMarshal().Value)
                {
                    item.Dispose();
                }
            })
            .AddTo(_disposables);

        editViewModel.Options.Select(x => x.MaxLayerCount)
            .DistinctUntilChanged()
            .Subscribe(TryApplyLayerCount);

        AdjustDurationToPointer.Subscribe(OnAdjustDurationToPointer);
        AdjustDurationToCurrent.Subscribe(OnAdjustDurationToCurrent);
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;

        AutoAdjustSceneDuration = editorConfig.GetObservable(EditorConfig.AutoAdjustSceneDurationProperty).ToReactiveProperty();
        AutoAdjustSceneDuration.Subscribe(b => editorConfig.AutoAdjustSceneDuration = b);

        IsLockCacheButtonEnabled = HoveredCacheBlock.Select(v => v is { IsLocked: false })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        IsUnlockCacheButtonEnabled = HoveredCacheBlock.Select(v => v is { IsLocked: true })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        DeleteAllFrameCache = new ReactiveCommandSlim()
            // EditorContext.FrameCacheManager.Valueは変更されるので、Clearは直接代入しない
            .WithSubscribe(() => EditorContext.FrameCacheManager.Value.Clear());

        DeleteFrameCache = HoveredCacheBlock.Select(v => v != null)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is { } block)
                {
                    FrameCacheManager manager = EditorContext.FrameCacheManager.Value;
                    if (block.IsLocked)
                    {
                        manager.Unlock(
                            block.StartFrame, block.StartFrame + block.LengthFrame);
                    }

                    manager.DeleteAndUpdateBlocks(
                        new[] { (block.StartFrame, block.StartFrame + block.LengthFrame) });
                }
            });

        LockFrameCache = HoveredCacheBlock.Select(v => v?.IsLocked == false)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is { } block)
                {
                    FrameCacheManager manager = EditorContext.FrameCacheManager.Value;
                    manager.Lock(
                        block.StartFrame, block.StartFrame + block.LengthFrame);

                    manager.UpdateBlocks();
                }
            });

        UnlockFrameCache = HoveredCacheBlock.Select(v => v?.IsLocked == true)
            .ToReactiveCommandSlim()
            .WithSubscribe(() =>
            {
                if (HoveredCacheBlock.Value is { } block)
                {
                    FrameCacheManager manager = EditorContext.FrameCacheManager.Value;
                    manager.Unlock(
                        block.StartFrame, block.StartFrame + block.LengthFrame);

                    manager.UpdateBlocks();
                }
            });

        // Todo: 設定からショートカットを変更できるようにする。
        KeyBindings = [];
        ConfigureKeyBindings();
    }

    private void OnAdjustDurationToPointer()
    {
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = ClickedFrame + TimeSpan.FromSeconds(1d / rate);

        RecordableCommands.Edit(Scene, Scene.DurationProperty, time)
            .WithStoables([Scene])
            .DoAndRecord(EditorContext.CommandRecorder);
    }

    private void OnAdjustDurationToCurrent()
    {
        int rate = Scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeSpan time = EditorContext.CurrentTime.Value + TimeSpan.FromSeconds(1d / rate);

        RecordableCommands.Edit(Scene, Scene.DurationProperty, time)
            .WithStoables([Scene])
            .DoAndRecord(EditorContext.CommandRecorder);
    }

    public Scene Scene { get; private set; }

    public PlayerViewModel Player { get; private set; }

    public EditViewModel EditorContext { get; private set; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReactiveCommand<ElementDescription> AddElement { get; } = new();

    [Obsolete("Use AddElement property instead.")]
    public ReactiveCommand<ElementDescription> AddLayer
    {
        get
        {
            Debug.Fail("Use AddElement property instead.");
            return AddElement;
        }
    }

    public CoreList<ElementViewModel> Elements { get; } = [];

    [Obsolete("Use Elements property instead.")]
    public CoreList<ElementViewModel> Layers
    {
        get
        {
            Debug.Fail("Use Elements property instead.");
            return Elements;
        }
    }

    public CoreList<InlineAnimationLayerViewModel> Inlines { get; } = [];

    public CoreList<LayerHeaderViewModel> LayerHeaders { get; } = [];

    public ReactiveCommand Paste { get; } = new();

    public ReactiveCommand<(TimeRange Range, int ZIndex)> ScrollTo { get; } = new();

    public ReactiveCommandSlim AdjustDurationToPointer { get; } = new();

    public ReactiveCommandSlim AdjustDurationToCurrent { get; } = new();

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

    public IReactiveProperty<TimelineOptions> Options => EditorContext.Options;

    public ToolTabExtension Extension => TimelineTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Bottom;

    public IObservable<LayerHeaderViewModel> LayerHeightChanged => _layerHeightChanged;

    public string Header => Strings.Timeline;

    public List<KeyBinding> KeyBindings { get; }

    public void Dispose()
    {
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
    }

    // ClickedPositionから最後にクリックしたレイヤーを計算します。
    public int CalculateClickedLayer()
    {
        return ToLayerNumber(ClickedPosition.Y);
    }

    // zindexまでLayerHeaderを追加します。
    private void AddLayerHeaders(int count)
    {
        if (LayerHeaders.Count != 0)
        {
            LayerHeaderViewModel last = LayerHeaders[LayerHeaders.Count - 1];

            for (int i = last.Number.Value + 1; i < count; i++)
            {
                LayerHeaders.Add(new LayerHeaderViewModel(i, this));
            }

            if (Options.Value.MaxLayerCount != LayerHeaders.Count)
            {
                Options.Value = Options.Value with
                {
                    MaxLayerCount = LayerHeaders.Count
                };

                _logger.LogDebug("The number of layers has been changed. ({Count})", count);
            }
        }
    }

    private void TryApplyLayerCount(int count)
    {
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
                Options.Value = Options.Value with
                {
                    MaxLayerCount = LayerHeaders.Count
                };

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
        if (json.TryGetPropertyValue(nameof(LayerHeaders), out JsonNode? layersNode)
            && layersNode is JsonArray layersArray)
        {
            foreach ((LayerHeaderViewModel layer, JsonObject item) in layersArray.OfType<JsonObject>()
                .Select(v => v.TryGetPropertyValueAsJsonValue(nameof(LayerHeaderViewModel.Number), out int number) ? (number, v) : (-1, null))
                .Where(v => v.Item2 != null)
                .Join(
                    LayerHeaders,
                    x => x.Item1,
                    y => y.Number.Value,
                    (x, y) => (y, x.Item2!)))
            {
                layer.ReadFromJson(item);
            }
        }

        if (json.TryGetPropertyValue(nameof(Inlines), out JsonNode? inlinesNode)
            && inlinesNode is JsonArray inlinesArray)
        {
            RestoreInlineAnimation(inlinesArray);
        }
    }

    private void RestoreInlineAnimation(JsonArray inlinesArray)
    {
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
            IAbstractAnimatableProperty? anmProp = null;
            Animatable? animatable = null;

            void FindAndSetAncestor(Span<object> span, KeyFrameAnimation kfAnm)
            {
                for (int i = 0; i < span.Length; i++)
                {
                    switch (span[i])
                    {
                        case IAbstractAnimatableProperty anmProp2 when ReferenceEquals(anmProp2.Animation, kfAnm):
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
                }
                else if (animatable != null)
                {
                    try
                    {
                        Type type = typeof(AnimatableCorePropertyImpl<>).MakeGenericType(anm.Property.PropertyType);
                        var createdProp = (IAbstractAnimatableProperty)Activator.CreateInstance(type, anm.Property, animatable)!;
                        AttachInline(createdProp!, element);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An exception occurred while restoring the UI.");
                    }
                }
            }
        }
    }

    public void WriteToJson(JsonObject json)
    {
        var array = new JsonArray();
        array.AddRange(LayerHeaders.Where(v => v.ShouldSaveState())
            .Select(v =>
            {
                var obj = new JsonObject
                {
                    [nameof(LayerHeaderViewModel.Number)] = v.Number.Value
                };
                v.WriteToJson(obj);
                return obj;
            }));

        json[nameof(LayerHeaders)] = array;

        var inlines = new JsonArray();
        foreach (InlineAnimationLayerViewModel item in Inlines.OrderBy(v => v.Index.Value))
        {
            if (item.Property.Animation is KeyFrameAnimation { Id: Guid anmId })
            {
                Guid elementId = item.Element.Model.Id;

                inlines.Add(new JsonObject
                {
                    ["AnimationId"] = anmId,
                    ["ElementId"] = elementId
                });
            }
        }

        json[nameof(Inlines)] = inlines;
    }

    public void AttachInline(IAbstractAnimatableProperty property, Element element)
    {
        if (!Inlines.Any(x => x.Element.Model == element && x.Property == property)
            && GetViewModelFor(element) is { } viewModel)
        {
            // タイムラインのタブを開く
            Type type = typeof(InlineAnimationLayerViewModel<>).MakeGenericType(property.PropertyType);
            if (Activator.CreateInstance(type, property, this, viewModel) is InlineAnimationLayerViewModel anmTimelineViewModel)
            {
                Inlines.Add(anmTimelineViewModel);
            }
        }
    }

    public void DetachInline(InlineAnimationLayerViewModel item)
    {
        if (item.LayerHeader.Value is { } layerHeader)
        {
            layerHeader.Inlines.Remove(item);
        }

        Inlines.Remove(item);

        item.Dispose();
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

    public bool AnySelected(ElementViewModel? exclude = null)
    {
        foreach (ElementViewModel item in Elements)
        {
            if ((exclude == null || exclude != item) && item.IsSelected.Value)
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<ElementViewModel> GetSelected(ElementViewModel? exclude = null)
    {
        foreach (ElementViewModel item in Elements)
        {
            if ((exclude == null || exclude != item) && item.IsSelected.Value)
            {
                yield return item;
            }
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

    // Todo: 設定からショートカットを変更できるようにする。
    private void ConfigureKeyBindings()
    {
        static KeyBinding KeyBinding(Key key, KeyModifiers modifiers, ICommand command)
        {
            return new KeyBinding
            {
                Gesture = new KeyGesture(key, modifiers),
                Command = command
            };
        }

        PlatformHotkeyConfiguration? keyConf = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (keyConf != null)
        {
            KeyBindings.AddRange(keyConf.Paste.Select(i => new KeyBinding
            {
                Command = Paste,
                Gesture = i
            }));
        }
        else
        {
            KeyBindings.Add(KeyBinding(Key.V, keyConf?.CommandModifiers ?? KeyModifiers.Control, Paste));
        }

        //KeyBindings.Add(KeyBinding(Key.))
    }

    private sealed class TrackedLayerTopObservable(int layerNum, TimelineViewModel timeline) : LightweightObservableBase<double>, IDisposable
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
