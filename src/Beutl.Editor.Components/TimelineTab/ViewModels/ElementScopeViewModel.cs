using Avalonia;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.ProjectSystem;

using Reactive.Bindings;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed class ElementScopeViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private PortalObject? _model;

    public ElementScopeViewModel(Element element, ElementViewModel parent)
    {
        Model = element;
        Parent = parent;
        element.Objects.Attached += OnChildrenAttached;
        element.Objects.Detached += OnChildrenDetached;
        foreach (EngineObject item in element.Objects)
        {
            if (item is PortalObject portal)
                portal.Count.ValueChanged += OnPortalCountChanged;
        }
        Update();

        Margin = parent.Margin.CombineLatest(parent.BorderMargin)
            .Select(t => t.First + t.Second)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Width = parent.Width
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        IObservable<int> zIndex = element.GetObservable(Element.ZIndexProperty);
        IObservable<(int EndZIndex, int ZIndex)> zIndexTuple = zIndex
            .CombineLatest(Count)
            .Select(t => (t.First + t.Second + 1, t.First));

        Height = zIndexTuple.Select(t =>
            {
                // CountがZero
                if (t.EndZIndex - 1 == t.ZIndex)
                {
                    return Observable.ReturnThenNever(0d);
                }
                else
                {
                    return parent.Timeline.GetTrackedLayerTopObservable(t.ZIndex)
                        .CombineLatest(parent.Timeline.GetTrackedLayerTopObservable(t.EndZIndex))
                        .Select(t => t.Second - t.First);
                }
            })
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(_disposables);
    }

    public Func<(Thickness Margin, double Width, double Height), CancellationToken, Task> AnimationRequested { get; set; } = (_, _) => Task.CompletedTask;

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveProperty<double> Height { get; }

    public ReactivePropertySlim<int> Count { get; } = new();

    public ElementViewModel Parent { get; }

    public Element Model { get; }

    private void OnChildrenDetached(EngineObject obj)
    {
        if (obj is PortalObject portal)
        {
            if (ReferenceEquals(_model, portal))
            {
                Update();
            }

            portal.Count.ValueChanged -= OnPortalCountChanged;
        }
    }

    private void OnChildrenAttached(EngineObject obj)
    {
        if (obj is PortalObject portal)
        {
            if (_model == null || _model.Count.CurrentValue < portal.Count.CurrentValue)
            {
                _model = portal;
                Count.Value = portal.Count.CurrentValue;
            }

            portal.Count.ValueChanged += OnPortalCountChanged;
        }
    }

    private void OnPortalCountChanged(object? sender, PropertyValueChangedEventArgs<int> e)
    {
        if (e.Property.GetOwnerObject() is PortalObject portal)
        {
            int newCount = portal.Count.CurrentValue;
            if (Count.Value < newCount)
            {
                _model = portal;
                Count.Value = newCount;
            }
            else if (ReferenceEquals(_model, portal))
            {
                Update();
            }
        }
    }

    private void Update()
    {
        int maxCount = int.MinValue;
        PortalObject? model = null;
        foreach (EngineObject item in Model.Objects)
        {
            if (item is PortalObject portal
                && maxCount < portal.Count.CurrentValue)
            {
                model = portal;
                maxCount = portal.Count.CurrentValue;
            }
        }

        if (model != null)
        {
            _model = model;
            Count.Value = maxCount;
            return;
        }

        _model = null;
        Count.Value = 0;
    }

    public void Dispose()
    {
        foreach (EngineObject item in Model.Objects)
        {
            if (item is PortalObject portal)
            {
                portal.Count.ValueChanged -= OnPortalCountChanged;
            }
        }

        Model.Objects.Attached -= OnChildrenAttached;
        Model.Objects.Detached -= OnChildrenDetached;

        _disposables.Dispose();
        Count.Dispose();
        AnimationRequested = (_, _) => Task.CompletedTask;
    }

    public async Task AnimationRequest(PrepareAnimationContext context, CancellationToken cancellationToken = default)
    {
        TimelineTabViewModel timeline = Parent.Timeline;
        var margin = new Thickness(
            Model.Start.TimeToPixel(timeline.Options.Value.Scale),
            timeline.CalculateLayerTop(Model.ZIndex), 0, 0);

        double width = Model.Length.TimeToPixel(timeline.Options.Value.Scale);
        double height = 0;

        if (Count.Value > 0)
        {
            height = timeline.CalculateLayerTop(Model.ZIndex + Count.Value + 1) - margin.Top;
        }

        Margin.Value = context.Margin;
        Width.Value = context.Width;
        Height.Value = context.Height;

        await AnimationRequested((margin, width, height), cancellationToken);
        Margin.Value = margin;
        Width.Value = width;
        Height.Value = height;
    }

    public PrepareAnimationContext PrepareAnimation()
    {
        return new PrepareAnimationContext(Margin.Value, Width.Value, Height.Value);
    }

    public record struct PrepareAnimationContext(Thickness Margin, double Width, double Height);
}
