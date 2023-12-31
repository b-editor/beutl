using System.ComponentModel;

using Avalonia;

using Beutl.Operation;
using Beutl.ProjectSystem;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class ElementScopeViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private TakeAfterOperator? _model;

    public ElementScopeViewModel(Element element, ElementViewModel parent)
    {
        Model = element;
        Parent = parent;
        element.GetObservable(Element.UseNodeProperty)
            .Subscribe(OnUseNodePropertyChanged)
            .DisposeWith(_disposables);

        element.Operation.Children.Attached += OnChildrenAttached;
        element.Operation.Children.Detached += OnChildrenDetached;
        foreach (SourceOperator item in element.Operation.Children)
        {
            if (item is TakeAfterOperator takeAfter)
                takeAfter.PropertyChanged += OnTakeAfterPropertyChanged;
        }

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
                    return Observable.Return(0d);
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

    public ElementViewModel Parent { get; private set; }

    public Element Model { get; private set; }

    private void OnChildrenDetached(SourceOperator obj)
    {
        if (obj is TakeAfterOperator takeAfter)
        {
            if (ReferenceEquals(_model, takeAfter))
            {
                Update();
            }

            takeAfter.PropertyChanged -= OnTakeAfterPropertyChanged;
        }
    }

    private void OnChildrenAttached(SourceOperator obj)
    {
        if (obj is TakeAfterOperator takeAfter)
        {
            if (_model == null || _model.Count < takeAfter.Count)
            {
                _model = takeAfter;
                Count.Value = takeAfter.Count;
            }

            takeAfter.PropertyChanged += OnTakeAfterPropertyChanged;
        }
    }

    private void OnTakeAfterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is TakeAfterOperator takeAfter
            && e is CorePropertyChangedEventArgs<int> ev
            && ev.Property == TakeAfterOperator.CountProperty)
        {
            if (Count.Value < ev.NewValue)
            {
                _model = takeAfter;
                Count.Value = takeAfter.Count;
            }
            else if (ReferenceEquals(_model, takeAfter))
            {
                Update();
            }
        }
    }

    private void OnUseNodePropertyChanged(bool obj)
    {
        Update();
    }

    private void Update()
    {
        if (!Model.UseNode)
        {
            int maxCount = int.MinValue;
            TakeAfterOperator? model = null;
            foreach (SourceOperator item in Model.Operation.Children)
            {
                if (item is TakeAfterOperator takeAfterOperator
                    && maxCount < takeAfterOperator.Count)
                {
                    model = takeAfterOperator;
                    maxCount = takeAfterOperator.Count;
                }
            }

            if (model != null)
            {
                _model = model;
                Count.Value = maxCount;
                return;
            }
        }

        _model = null;
        Count.Value = 0;
    }

    public void Dispose()
    {
        foreach (SourceOperator item in Model.Operation.Children)
        {
            if (item is TakeAfterOperator takeAfterOperator)
            {
                takeAfterOperator.PropertyChanged -= OnTakeAfterPropertyChanged;
            }
        }

        Model.Operation.Children.Attached -= OnChildrenAttached;
        Model.Operation.Children.Detached -= OnChildrenDetached;

        _disposables.Dispose();
        Count.Dispose();
        Model = null!;
        _model = null!;
        Parent = null!;
        AnimationRequested = (_, _) => Task.CompletedTask;
    }

    public async Task AnimationRequest(PrepareAnimationContext context, CancellationToken cancellationToken = default)
    {
        TimelineViewModel timeline = Parent.Timeline;
        var margin = new Thickness(
            Model.Start.ToPixel(timeline.Options.Value.Scale),
            timeline.CalculateLayerTop(Model.ZIndex), 0, 0);

        double width = Model.Length.ToPixel(timeline.Options.Value.Scale);
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
