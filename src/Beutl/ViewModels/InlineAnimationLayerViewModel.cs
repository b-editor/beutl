using System.Collections.Specialized;
using System.Reactive.Subjects;

using Avalonia;

using Beutl.Framework;
using Beutl.Reactive;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class InlineAnimationLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<double> _heightSubject = new();
    private double _height;

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

        layer.Model.GetObservable(ProjectSystem.Layer.ZIndexProperty)
            .Subscribe(OnZIndexChanged)
            .DisposeWith(_disposables);

        Margin = new TrackedInlineLayerTopObservable(this)
            .Select(x => new Thickness(0, x, 0, 0))
            .CombineLatest(layer.Margin)
            .Select(x => x.First + x.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Header = PropertyEditorService.GetPropertyName(property.Property);

        Close = new ReactiveCommand()
            .WithSubscribe(() => Timeline.DetachInline(this))
            .DisposeWith(_disposables);
    }

    public Func<Thickness, CancellationToken, Task> AnimationRequested { get; set; } = (_, _) => Task.CompletedTask;

    public IAbstractAnimatableProperty Property { get; }

    public TimelineViewModel Timeline { get; }

    public TimelineLayerViewModel Layer { get; }

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactivePropertySlim<int> Index { get; } = new();
    
    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

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

    public ReactivePropertySlim<LayerHeaderViewModel?> LayerHeader { get; set; } = new();

    public event EventHandler<(double OldHeight, double NewHeight)>? HeightChanged;

    private void OnZIndexChanged(int zIndex)
    {
        LayerHeaderViewModel? newLH = Timeline.LayerHeaders.FirstOrDefault(i => i.Number.Value == zIndex);

        LayerHeader.Value?.Inlines.Remove(this);
        newLH?.Inlines.Add(this);

        LayerHeader.Value = newLH;
    }

    public void NotifyDetached()
    {
        LayerHeader.Value?.Inlines.Remove(this);
        LayerHeader.Value = null;
    }

    public void NotifyAttached(int zIndex)
    {
        LayerHeaderViewModel? newLH = Timeline.LayerHeaders.FirstOrDefault(i => i.Number.Value == zIndex);
        newLH?.Inlines.Add(this);
        LayerHeader.Value = newLH;
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
            await AnimationRequested(newMargin, cancellationToken);
            Margin.Value = newMargin;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public record struct PrepareAnimationContext(Thickness Margin);

    private sealed class TrackedInlineLayerTopObservable : LightweightObservableBase<double>
    {
        private readonly InlineAnimationLayerViewModel _inline;
        private IDisposable? _disposable1;
        private IDisposable? _disposable2;
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
                _prevLayerHeader.Inlines.CollectionChanged -= OnInlinesCollectionChanged;
            }

            _prevLayerHeader = obj;

            if (obj != null)
            {
                obj.Inlines.CollectionChanged += OnInlinesCollectionChanged;
            }

            PublishValue();
        }

        private void OnInlinesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (_prevIndex >= e.NewStartingIndex)
                    {
                        PublishValue();
                    }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (_prevIndex >= e.OldStartingIndex)
                    {
                        PublishValue();
                    }
                    break;
                case NotifyCollectionChangedAction.Replace:
                    throw new InvalidOperationException("Not supported action.");
                case NotifyCollectionChangedAction.Move:
                    if (_prevIndex != e.OldStartingIndex
                        && ((_prevIndex > e.OldStartingIndex && _prevIndex <= e.NewStartingIndex)
                        || (_prevIndex < e.OldStartingIndex && _prevIndex >= e.NewStartingIndex)))
                    {
                        PublishValue();
                    }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    break;
            }
        }

        private void OnIndexChanged(int obj)
        {
            if (_prevIndex != obj)
            {
                _prevIndex = obj;
                PublishValue();
            }
        }

        private void PublishValue()
        {
            if (_prevLayerHeader != null)
            {
                _prevIndex = _prevLayerHeader.Inlines.IndexOf(_inline);
                PublishNext(_prevLayerHeader.CalculateInlineTop(_prevIndex) + Helper.LayerHeight);
            }
        }

        protected override void Subscribed(IObserver<double> observer, bool first)
        {
            PublishValue();
        }
    }
}
