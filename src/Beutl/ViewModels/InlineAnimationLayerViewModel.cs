using Avalonia;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Framework;
using Beutl.Reactive;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class InlineAnimationLayerViewModel<T> : InlineAnimationLayerViewModel
{
    public InlineAnimationLayerViewModel(IAbstractAnimatableProperty<T> property, TimelineViewModel timeline, TimelineLayerViewModel layer)
        : base(property, timeline, layer)
    {
    }

    public override void InsertKeyFrame(Easing easing, TimeSpan keyTime)
    {
        if (Property.Animation is KeyFrameAnimation<T> kfAnimation
            && !kfAnimation.KeyFrames.Any(x => x.KeyTime == keyTime))
        {
            var keyframe = new KeyFrame<T>()
            {
                Value = kfAnimation.Interpolate(keyTime),
                Easing = easing,
                KeyTime = keyTime
            };

            var command = new AddKeyFrameCommand(kfAnimation.KeyFrames, keyframe);
            command.DoAndRecord(CommandRecorder.Default);
        }
    }

    private sealed class AddKeyFrameCommand : IRecordableCommand
    {
        private readonly KeyFrames _keyFrames;
        private readonly IKeyFrame _keyFrame;

        public AddKeyFrameCommand(KeyFrames keyFrames, IKeyFrame keyFrame)
        {
            _keyFrames = keyFrames;
            _keyFrame = keyFrame;
        }

        public void Do()
        {
            _keyFrames.Add(_keyFrame, out _);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _keyFrames.Remove(_keyFrame);
        }
    }
}

public abstract class InlineAnimationLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private LayerHeaderViewModel? _lastLayerHeader;
    private IDisposable? _innerDisposable;

    protected InlineAnimationLayerViewModel(
        IAbstractAnimatableProperty property,
        TimelineViewModel timeline,
        TimelineLayerViewModel layer)
    {
        Property = property;
        Timeline = timeline;
        Layer = layer;

        Layer.LayerHeader.Subscribe(OnLayerHeaderChanged).DisposeWith(_disposables);

        Margin = new TrackedInlineLayerTopObservable(this)
            .Select(x => new Thickness(0, x, 0, 0))
            .CombineLatest(layer.Margin)
            .Select(x => x.First + x.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        // Widthプロパティを構成
        Timeline.Options.Subscribe(_ => UpdateWidth()).DisposeWith(_disposables);

        CorePropertyMetadata metadata = property.Property.GetMetadata<CorePropertyMetadata>(property.ImplementedType);
        Header = metadata.DisplayAttribute?.GetName() ?? property.Property.Name;

        Close = new ReactiveCommand()
            .WithSubscribe(() => Timeline.DetachInline(this))
            .DisposeWith(_disposables);

        Property.ObserveAnimation.CombineWithPrevious()
            .Subscribe(t =>
            {
                if (t.OldValue != null)
                {
                    t.OldValue.Invalidated -= OnAnimationInvalidated;
                    _innerDisposable?.Dispose();
                    _innerDisposable = null;
                    ClearItems();
                }

                if (t.NewValue != null)
                {
                    t.NewValue.Invalidated += OnAnimationInvalidated;
                    if (t.NewValue is IKeyFrameAnimation kfAnimation)
                    {
                        _innerDisposable = kfAnimation.KeyFrames.ForEachItem(
                            (idx, item) => Items.Insert(idx, new InlineKeyFrameViewModel(item, kfAnimation, this)),
                            (idx, _) =>
                            {
                                InlineKeyFrameViewModel item = Items[idx];
                                item.Dispose();
                                Items.RemoveAt(idx);
                            },
                            ClearItems);
                    }
                }
            })
            .DisposeWith(_disposables);
    }

    public Func<Thickness, CancellationToken, Task>? AnimationRequested { get; set; }

    public IAbstractAnimatableProperty Property { get; }

    public TimelineViewModel Timeline { get; }

    public TimelineLayerViewModel Layer { get; }

    public CoreList<InlineKeyFrameViewModel> Items { get; } = new();

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactivePropertySlim<double> Width { get; } = new();

    public ReactivePropertySlim<int> Index { get; } = new();

    public string Header { get; }

    public ReactiveCommand Close { get; }

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader => Layer.LayerHeader;

    public abstract void InsertKeyFrame(Easing easing, TimeSpan keyTime);

    private void OnLayerHeaderChanged(LayerHeaderViewModel? obj)
    {
        _lastLayerHeader?.Inlines.Remove(this);
        _lastLayerHeader = obj;
        _lastLayerHeader?.Inlines.Add(this);
    }

    private void UpdateWidth()
    {
        TimeSpan duration = Property.Animation?.Duration ?? TimeSpan.Zero;
        Width.Value = duration.ToPixel(Timeline.Options.Value.Scale);
    }

    private void OnAnimationInvalidated(object? sender, EventArgs e)
    {
        UpdateWidth();
    }

    public PrepareAnimationContext PrepareAnimation()
    {
        return new PrepareAnimationContext(Margin.Value);
    }

    public async void AnimationRequest(PrepareAnimationContext context, Thickness layerMargin, CancellationToken cancellationToken = default)
    {
        if (LayerHeader.Value is { } layerHeader)
        {
            Index.Value = layerHeader.Inlines.IndexOf(this);
            double top = layerHeader.CalculateInlineTop(Index.Value) + Helper.LayerHeight;
            Thickness newMargin = new Thickness(0, top, 0, 0) + layerMargin;

            Margin.Value = context.Margin;
            if (AnimationRequested != null)
            {
                await AnimationRequested(newMargin, cancellationToken);
            }
            Margin.Value = newMargin;
        }
    }

    public void Dispose()
    {
        _innerDisposable?.Dispose();
        _disposables.Dispose();
        if (Property.Animation != null)
            Property.Animation.Invalidated -= OnAnimationInvalidated;

        ClearItems();

        GC.SuppressFinalize(this);
    }

    private void ClearItems()
    {
        foreach (InlineKeyFrameViewModel item in Items.GetMarshal().Value)
        {
            item.Dispose();
        }

        Items.Clear();
    }

    public record struct PrepareAnimationContext(Thickness Margin);

    private sealed class TrackedInlineLayerTopObservable : LightweightObservableBase<double>
    {
        private readonly InlineAnimationLayerViewModel _inline;
        private IDisposable? _disposable1;
        private IDisposable? _disposable2;
        private IDisposable? _disposable3;
        private int _prevIndex = -1;
        private LayerHeaderViewModel? _prevLayerHeader;

        public TrackedInlineLayerTopObservable(InlineAnimationLayerViewModel inline)
        {
            _inline = inline;
        }

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
            _disposable1 = _inline.LayerHeader
                .Subscribe(OnLayerHeaderChanged);

            _disposable2 = _inline.Index.Subscribe(OnIndexChanged);
        }

        private void OnLayerHeaderChanged(LayerHeaderViewModel? obj)
        {
            if (_prevLayerHeader != null)
            {
                _disposable3?.Dispose();
                _disposable3 = null;
            }

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
                _prevIndex = _prevLayerHeader.Inlines.IndexOf(_inline);

                double value = _prevLayerHeader.CalculateInlineTop(_prevIndex) + Helper.LayerHeight;
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
