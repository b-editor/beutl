using Avalonia;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Commands;
using Beutl.Reactive;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class InlineAnimationLayerViewModel<T>(
    IAbstractAnimatableProperty<T> property, TimelineViewModel timeline, ElementViewModel element)
    : InlineAnimationLayerViewModel(property, timeline, element)
{
    public override void DropEasing(Easing easing, TimeSpan keyTime)
    {
        if (Property.Animation is KeyFrameAnimation<T> kfAnimation)
        {
            TimeSpan originalKeyTime = keyTime;
            keyTime = ConvertKeyTime(originalKeyTime, kfAnimation);
            Project? proj = Timeline.Scene.FindHierarchicalParent<Project>();
            int rate = proj?.GetFrameRate() ?? 30;

            TimeSpan threshold = TimeSpan.FromSeconds(1d / rate) * 3;

            IKeyFrame? keyFrame = kfAnimation.KeyFrames.FirstOrDefault(v => Math.Abs(v.KeyTime.Ticks - keyTime.Ticks) <= threshold.Ticks);
            if (keyFrame != null)
            {
                new ChangePropertyCommand<Easing>(keyFrame, KeyFrame.EasingProperty, easing, keyFrame.Easing)
                    .DoAndRecord(CommandRecorder.Default);
            }
            else
            {
                InsertKeyFrame(easing, originalKeyTime);
            }
        }
    }

    public override void InsertKeyFrame(Easing easing, TimeSpan keyTime)
    {
        if (Property.Animation is KeyFrameAnimation<T> kfAnimation)
        {
            keyTime = ConvertKeyTime(keyTime, kfAnimation);
            if (!kfAnimation.KeyFrames.Any(x => x.KeyTime == keyTime))
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
    }

    private sealed class AddKeyFrameCommand(KeyFrames keyFrames, IKeyFrame keyFrame) : IRecordableCommand
    {
        public void Do()
        {
            keyFrames.Add(keyFrame, out _);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            keyFrames.Remove(keyFrame);
        }
    }
}

public abstract class InlineAnimationLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly CompositeDisposable _innerDisposables = [];
    private readonly ReactivePropertySlim<bool> _useGlobalClock = new(true);
    private LayerHeaderViewModel? _lastLayerHeader;

    protected InlineAnimationLayerViewModel(
        IAbstractAnimatableProperty property,
        TimelineViewModel timeline,
        ElementViewModel element)
    {
        Property = property;
        Timeline = timeline;
        Element = element;

        Element.LayerHeader.Subscribe(OnLayerHeaderChanged).DisposeWith(_disposables);

        LeftMargin = _useGlobalClock.Select(v => !v ? element.BorderMargin : Observable.Return<Thickness>(default))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Margin = new TrackedInlineLayerTopObservable(this)
            .Select(x => new Thickness(0, x, 0, 0))
            .CombineLatest(element.Margin)
            .Select(x => x.First + x.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        // Widthプロパティを構成
        Timeline.Options.Subscribe(_ => UpdateWidth()).DisposeWith(_disposables);

        Header = property.DisplayName;

        Close = new ReactiveCommand()
            .WithSubscribe(() => Timeline.DetachInline(this))
            .DisposeWith(_disposables);

        Property.ObserveAnimation.CombineWithPrevious()
            .Subscribe(t =>
            {
                if (t.OldValue != null)
                {
                    t.OldValue.Invalidated -= OnAnimationInvalidated;
                    _innerDisposables.Clear();
                    ClearItems();
                }

                if (t.NewValue != null)
                {
                    t.NewValue.Invalidated += OnAnimationInvalidated;
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
                    }

                    ((CoreObject)t.NewValue).GetObservable(KeyFrameAnimation.UseGlobalClockProperty)
                        .Subscribe(v => _useGlobalClock.Value = v)
                        .DisposeWith(_innerDisposables);
                }
            })
            .DisposeWith(_disposables);
    }

    public Func<Thickness, Thickness, CancellationToken, Task>? AnimationRequested { get; set; }

    public IAbstractAnimatableProperty Property { get; }

    public TimelineViewModel Timeline { get; }

    public ElementViewModel Element { get; }

    [Obsolete("Use Element property instead.")]
    public ElementViewModel Layer => Element;

    public CoreList<InlineKeyFrameViewModel> Items { get; } = [];

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> LeftMargin { get; }

    public ReactivePropertySlim<double> Width { get; } = new();

    public ReactivePropertySlim<int> Index { get; } = new();

    public string Header { get; }

    public ReactiveCommand Close { get; }

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader => Element.LayerHeader;

    public abstract void DropEasing(Easing easing, TimeSpan keyTime);

    public abstract void InsertKeyFrame(Easing easing, TimeSpan keyTime);

    public TimeSpan ConvertKeyTime(TimeSpan globalkeyTime, IAnimation animation)
    {
        TimeSpan localKeyTime = globalkeyTime - Element.Model.Start;
        TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;

        int rate = Timeline.Scene?.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30;

        return keyTime.RoundToRate(rate);
    }

    public void RemoveKeyFrame(IKeyFrame keyFrame)
    {
        if (Property.Animation is IKeyFrameAnimation kfAnimation)
        {
            kfAnimation.KeyFrames.BeginRecord<IKeyFrame>()
                .Remove(keyFrame)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

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
        return new PrepareAnimationContext(Margin.Value, LeftMargin.Value);
    }

    public async void AnimationRequest(PrepareAnimationContext context, Thickness layerMargin, Thickness leftMargin, CancellationToken cancellationToken = default)
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
        _innerDisposables?.Dispose();
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

    public record struct PrepareAnimationContext(Thickness Margin, Thickness LeftMargin);

    private sealed class TrackedInlineLayerTopObservable(InlineAnimationLayerViewModel inline) : LightweightObservableBase<double>
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
