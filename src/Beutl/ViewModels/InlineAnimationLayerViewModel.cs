using System.Collections;
using System.Collections.Specialized;
using System.Reactive.Subjects;

using Avalonia;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Framework;
using Beutl.Reactive;
using Beutl.Services;
using Beutl.ViewModels.AnimationEditors;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class InlineAnimationLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<double> _heightSubject = new();
    private double _height;
    private LayerHeaderViewModel? _lastLayerHeader;

    public InlineAnimationLayerViewModel(
        IAbstractAnimatableProperty property,
        TimelineViewModel timeline,
        TimelineLayerViewModel layer)
    {
        Property = property;
        Timeline = timeline;
        Layer = layer;
        _height = Helper.LayerHeight;

        ObserveHeight = _heightSubject.ToReadOnlyReactivePropertySlim(_height).DisposeWith(_disposables);

        Layer.LayerHeader.Subscribe(OnLayerHeaderChanged).DisposeWith(_disposables);

        Margin = new TrackedInlineLayerTopObservable(this)
            .Select(x => new Thickness(0, x, 0, 0))
            .CombineLatest(layer.Margin)
            .Select(x => x.First + x.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        // Widthプロパティを構成
        property.Animation.Invalidated += OnAnimationInvalidated;
        Timeline.Options.Subscribe(_ => UpdateWidth()).DisposeWith(_disposables);

        CorePropertyMetadata metadata = property.Property.GetMetadata<CorePropertyMetadata>(property.ImplementedType);
        Header = metadata.DisplayAttribute?.GetName() ?? property.Property.Name;

        Close = new ReactiveCommand()
            .WithSubscribe(() => Timeline.DetachInline(this))
            .DisposeWith(_disposables);

        Property.Animation.Children.ForEachItem(
            (idx, item) => Items.Insert(idx, new InlineAnimationEditorViewModel(item, this)),
            (idx, _) =>
            {
                InlineAnimationEditorViewModel item = Items[idx];
                item.Dispose();
                Items.RemoveAt(idx);
            },
            () =>
            {
                foreach (InlineAnimationEditorViewModel item in Items.GetMarshal().Value)
                {
                    item.Dispose();
                }

                Items.Clear();
            });
    }

    public Func<Thickness, CancellationToken, Task>? AnimationRequested { get; set; }

    public IAbstractAnimatableProperty Property { get; }

    public TimelineViewModel Timeline { get; }

    public TimelineLayerViewModel Layer { get; }

    public CoreList<InlineAnimationEditorViewModel> Items { get; } = new();

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactivePropertySlim<double> Width { get; } = new();

    public ReactivePropertySlim<int> Index { get; } = new();

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactivePropertySlim<bool> ShowAnimationVisual { get; } = new(true);

    public string Header { get; }

    public double Height
    {
        get => _height;
        set
        {
            if (_height != value)
            {
                double old = _height;
                _height = value;
                _heightSubject.OnNext(value);
                HeightChanged?.Invoke(this, (old, value));
            }
        }
    }

    public ReactiveCommand Close { get; }

    public ReadOnlyReactivePropertySlim<double> ObserveHeight { get; }

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader => Layer.LayerHeader;

    public event EventHandler<(double OldHeight, double NewHeight)>? HeightChanged;

    public void AddAnimation(Easing easing)
    {
        CoreProperty? property = Property.Property;
        Type type = typeof(AnimationSpan<>).MakeGenericType(property.PropertyType);

        if (Property.Animation.Children is IList list
            && Activator.CreateInstance(type) is IAnimationSpan animation)
        {
            animation.Easing = easing;
            animation.Duration = TimeSpan.FromSeconds(2);
            object? value = Property.GetValue();

            if (value != null)
            {
                animation.Previous = value;
                animation.Next = value;
            }

            list.BeginRecord()
                .Add(animation)
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
        TimeSpan duration = Property.Animation.CalculateDuration();
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
        _disposables.Dispose();
        Property.Animation.Invalidated -= OnAnimationInvalidated;
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
