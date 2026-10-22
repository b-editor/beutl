using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed class InlineAnimationLayerViewModel : IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<InlineAnimationLayerViewModel>();
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _innerDisposables = [];
    private readonly ReactivePropertySlim<bool> _useGlobalClock = new(true);
    private LayerHeaderViewModel? _lastLayerHeader;
    private MediaTime _pointerPosition;
    private bool _disposed;

    public InlineAnimationLayerViewModel(
        IAnimatablePropertyAdapter property,
        TimelineTabViewModel timeline,
        ElementViewModel element)
    {
        Property = property;
        Timeline = timeline;
        Element = element;

        Element.LayerHeader.Subscribe(OnLayerHeaderChanged).DisposeWith(_disposables);

        LeftMargin = _useGlobalClock.Select(v => !v ? element.BorderMargin : Observable.ReturnThenNever<Thickness>(default))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Margin = new TrackedInlineLayerTopObservable(this)
            .Select(x => new Thickness(0, x, 0, 0))
            .CombineLatest(element.Margin)
            .Select(x => x.First + x.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Timeline.Options.Subscribe(_ => UpdateWidth()).DisposeWith(_disposables);

        Header = property.DisplayName;

        Close = new ReactiveCommand()
            .WithSubscribe(() => Timeline.DetachInline(this))
            .DisposeWith(_disposables);

        CopyAllKeyFramesCommand = new ReactiveCommand()
            .WithSubscribe(async () => await KeyFrameClipboardCommands.CopyAnimationAsync(Property.Animation!, _logger))
            .DisposeWith(_disposables);

        PasteKeyFrameAtCurrentPositionCommand = new ReactiveCommand()
            .WithSubscribe(async () => await PasteKeyFrameAtPositionAsync(_pointerPosition))
            .DisposeWith(_disposables);

        DeleteCurrentAnimationCommand = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                if (Property.Animation is { } animation)
                {
                    DeleteAnimation();
                }
            })
            .DisposeWith(_disposables);

        Property.ObserveAnimation.CombineWithPrevious()
            .Subscribe(OnAnimationChanged)
            .DisposeWith(_disposables);
    }

    private void OnAnimationChanged((IKeyFrameAnimation? OldValue, IKeyFrameAnimation? NewValue) t)
    {
        if (t.OldValue != null)
        {
            t.OldValue.Edited -= OnAnimationEdited;
            _innerDisposables.Clear();
            ClearItems();
        }

        if (t.NewValue != null)
        {
            t.NewValue.Edited += OnAnimationEdited;
            if (t.NewValue is IKeyFrameAnimation kfAnimation)
            {
                kfAnimation.KeyFrames.ForEachItem(
                        (idx, item) => Items.Insert(idx, new InlineKeyFrameViewModel(item, kfAnimation, this)),
                        (idx, _) =>
                        {
                            InlineKeyFrameViewModel item = Items[idx];
                            item.Dispose();
                            Items.RemoveAt(idx);
                        },
                        ClearItems)
                    .DisposeWith(_innerDisposables);
                kfAnimation.KeyFrames.CollectionChangedAsObservable()
                    .Subscribe(_ => UpdateWidth()).DisposeWith(_innerDisposables);
            }

            t.NewValue.ObserveUseGlobalClock()
                .Subscribe(v => _useGlobalClock.Value = v)
                .DisposeWith(_innerDisposables);

            if (t.NewValue is IHierarchical hierarchical)
            {
                hierarchical.DetachedFromHierarchy += OnAnimationDetached;
                Disposable.Create(hierarchical, h => h.DetachedFromHierarchy -= OnAnimationDetached)
                    .DisposeWith(_innerDisposables);
            }
        }

        // nullが代入されたときDetachedFromHierarchyより先に_innerDisposables.Disposeが呼び出されるので、手動でDetachInlineを呼び出す
        if (t.OldValue != null && t.NewValue == null)
        {
            Timeline.DetachInline(this);
        }
    }

    public Func<Thickness, Thickness, CancellationToken, Task>? AnimationRequested { get; set; }

    public IAnimatablePropertyAdapter Property { get; }

    public TimelineTabViewModel Timeline { get; }

    public ElementViewModel Element { get; }

    // Inline keyframe edits are refused while the clip or its layer is locked.
    public bool IsEditable => Element.IsEditable.Value;

    public CoreList<InlineKeyFrameViewModel> Items { get; } = [];

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> LeftMargin { get; }

    public ReactivePropertySlim<double> Width { get; } = new();

    public ReactivePropertySlim<int> Index { get; } = new();

    public string Header { get; }

    public ReactiveCommand Close { get; }

    public ReactiveCommand CopyAllKeyFramesCommand { get; }

    public ReactiveCommand PasteKeyFrameAtCurrentPositionCommand { get; }

    public ReactiveCommand DeleteCurrentAnimationCommand { get; }

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader => Element.LayerHeader;

    public void DropEasing(Easing easing, MediaTime keyTime)
    {
        if (!IsEditable) return;
        if (Property.Animation is IKeyFrameAnimation kfAnimation)
        {
            KeyFrameClipboardCommands.DropEasing(
                kfAnimation, Timeline.Scene, Timeline.EditorContext.GetRequiredService<HistoryManager>(), easing,
                keyTime, ConvertKeyTime(keyTime, kfAnimation), InsertKeyFrame, null);
        }
    }

    public void InsertKeyFrame(Easing easing, MediaTime keyTime)
    {
        if (!IsEditable) return;
        if (Property.Animation is not IKeyFrameAnimation animation) return;
        HistoryManager history = Timeline.EditorContext.GetRequiredService<HistoryManager>();

        AnimationOperations.InsertKeyFrame(
            animation: animation,
            easing: easing,
            keyTime: keyTime,
            logger: _logger);
        history.Commit(CommandNames.InsertKeyFrame);
    }

    public MediaTime ConvertKeyTime(MediaTime globalkeyTime, IKeyFrameAnimation animation)
    {
        return AnimationOperations.ConvertKeyTime(animation, globalkeyTime, Element.Model, Timeline.Scene);
    }

    private void PasteAnimation(string json)
    {
        if (!IsEditable) return;
        KeyFrameClipboardCommands.PasteAnimation(Timeline.EditorContext, Property.Animation!, json, _logger);
    }

    private void PasteKeyFrame(string json, MediaTime pointerPosition)
    {
        if (!IsEditable) return;
        KeyFrameClipboardCommands.PasteKeyFrame(
            Timeline.EditorContext, Property.Animation!, json, animation => ConvertKeyTime(pointerPosition, animation),
            easing => InsertKeyFrame(easing, pointerPosition), _logger);
    }

    public void DeleteAnimation()
    {
        if (!IsEditable) return;
        HistoryManager history = Timeline.EditorContext.GetRequiredService<HistoryManager>();

        Property.Animation = null;
        history.Commit(CommandNames.DeleteAnimation);
    }

    internal async Task PasteKeyFrameAtPositionAsync(MediaTime pointerPosition, IClipboard? clipboard = null)
    {
        clipboard ??= ClipboardHelper.GetClipboard();
        if (_disposed || !IsEditable || clipboard == null || Property.Animation is not IKeyFrameAnimation animation) return;

        await KeyFrameClipboardCommands.PasteAtPositionAsync(
            clipboard,
            () => !_disposed && IsEditable && ReferenceEquals(Property.Animation, animation),
            static () => true,
            json => PasteKeyFrame(json, pointerPosition),
            json => PasteSelection(json, ConvertKeyTime(pointerPosition, animation)),
            PasteAnimation,
            _logger);
    }

    internal void PasteSelection(string json, MediaTime keyTime)
    {
        if (_disposed || !IsEditable || Property.Animation is not IKeyFrameAnimation animation) return;
        if (KeyFrameSelectionClipboard.Read(json) is not { } source
            || source.Type != animation.GetType() || source.Keys.Length == 0)
        {
            NotificationService.ShowWarning(Strings.Paste, MessageStrings.InvalidKeyframeDataFormat);
            return;
        }
        KeyFrameSelectionClipboard.Paste(animation, source.Keys, keyTime,
            Timeline.EditorContext.GetRequiredService<HistoryManager>());
    }

    public bool HandleDragOver(DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(BeutlDataFormats.Easing)) return false;

        e.DragEffects = DragDropEffects.Copy;
        return true;
    }

    public bool HandleDrop(DragEventArgs e, double positionX)
    {
        if (!KeyFrameClipboardCommands.TryCreateDroppedEasing(e.DataTransfer, out Easing? easing)) return false;

        float scale = Timeline.Options.Value.Scale;
        MediaTime time = positionX.PixelToTime(scale);
        DropEasing(easing, time);
        return true;
    }

    public void UpdatePointerPosition(double positionX)
    {
        float scale = Timeline.Options.Value.Scale;
        _pointerPosition = positionX.PixelToTime(scale);
    }

    private void OnLayerHeaderChanged(LayerHeaderViewModel? obj)
    {
        _lastLayerHeader?.Inlines.Remove(this);
        _lastLayerHeader = obj;
        _lastLayerHeader?.Inlines.Add(this);
    }

    private void UpdateWidth()
    {
        MediaTime duration = Property.Animation?.Duration ?? MediaTime.Zero;
        Width.Value = duration.TimeToPixel(Timeline.Options.Value.Scale);
    }

    private void OnAnimationEdited(object? sender, EventArgs e)
    {
        UpdateWidth();
    }

    private void OnAnimationDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        Dispatcher.UIThread.Post(() => Timeline.DetachInline(this));
    }

    public PrepareAnimationContext PrepareAnimation()
    {
        return new PrepareAnimationContext(Margin.Value, LeftMargin.Value);
    }

    public async void AnimationRequest(PrepareAnimationContext context, Thickness layerMargin, Thickness leftMargin,
        CancellationToken cancellationToken = default)
    {
        if (LayerHeader.Value is { } layerHeader)
        {
            Index.Value = layerHeader.Inlines.IndexOf(this);
            double top = layerHeader.CalculateInlineTop(Index.Value) + FrameNumberHelper.LayerHeight;
            Thickness newMargin = new Thickness(0, top, 0, 0) + layerMargin;
            Thickness newLeftMargin = Property.Animation?.UseGlobalClock == true ? default : leftMargin;

            Margin.Value = context.Margin;
            LeftMargin.Value = context.LeftMargin;
            if (AnimationRequested != null)
            {
                await AnimationRequested(newMargin, newLeftMargin, cancellationToken);
            }

            Margin.Value = newMargin;
            LeftMargin.Value = newLeftMargin;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _innerDisposables.Dispose();
        _disposables.Dispose();
        if (Property.Animation != null)
            Property.Animation.Edited -= OnAnimationEdited;

        ClearItems();
    }

    private void ClearItems()
    {
        foreach (InlineKeyFrameViewModel item in Items.AsSpan())
        {
            item.Dispose();
        }

        Items.Clear();
    }

    public record struct PrepareAnimationContext(Thickness Margin, Thickness LeftMargin);

    private sealed class TrackedInlineLayerTopObservable(InlineAnimationLayerViewModel inline)
        : LightweightObservableBase<double>
    {
        private IDisposable? _disposable1;
        private IDisposable? _disposable2;
        private IDisposable? _disposable3;
        private int _prevIndex = -1;
        private LayerHeaderViewModel? _prevLayerHeader;

        protected override void Deinitialize()
        {
            _disposable1?.Dispose();
            _disposable2?.Dispose();
            _disposable3?.Dispose();
            _disposable1 = null;
            _disposable2 = null;
            _disposable3 = null;
        }

        protected override void Initialize()
        {
            _disposable1 = inline.LayerHeader
                .Subscribe(OnLayerHeaderChanged);

            _disposable2 = inline.Index.Subscribe(OnIndexChanged);
        }

        private void OnLayerHeaderChanged(LayerHeaderViewModel? obj)
        {
            _disposable3?.Dispose();
            _disposable3 = null;

            _prevLayerHeader = obj;

            if (obj != null)
            {
                _disposable3 = obj.Height.Subscribe(OnLayerHeaderHeightChanged);
            }
        }

        private void OnLayerHeaderHeightChanged(double obj)
        {
            PublishValue();
        }

        private void OnIndexChanged(int obj)
        {
            if (_prevIndex != obj)
            {
                _prevIndex = obj;
                PublishValue();
            }
        }

        private void PublishValue(IObserver<double>? observer = null)
        {
            if (_prevLayerHeader != null)
            {
                _prevIndex = _prevLayerHeader.Inlines.IndexOf(inline);

                double value = _prevLayerHeader.CalculateInlineTop(_prevIndex) + FrameNumberHelper.LayerHeight;
                if (observer == null)
                {
                    PublishNext(value);
                }
                else
                {
                    observer.OnNext(value);
                }
            }
        }

        protected override void Subscribed(IObserver<double> observer, bool first)
        {
            PublishValue(observer);
        }
    }
}
