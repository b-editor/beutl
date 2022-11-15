using System.Reactive.Subjects;

using Beutl.Framework;
using Beutl.ProjectSystem;

using OpenCvSharp;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class InlineAnimationLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<double> _heightSubject = new();
    private double _height;
    private LayerHeaderViewModel? _layerHeader;

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

        var zIndexSubject = layer.Model.GetObservable(ProjectSystem.Layer.ZIndexProperty);
        zIndexSubject.Subscribe(number =>
        {
            LayerHeaderViewModel? newLH = Timeline.LayerHeaders.FirstOrDefault(i => i.Number.Value == number);

            if (_layerHeader != null)
            {
                _layerHeader.Inlines.Remove(this);
            }

            if (newLH != null)
            {
                newLH.Inlines.Add(this);
            }
            _layerHeader = newLH;
        }).DisposeWith(_disposables);
    }

    public IAbstractAnimatableProperty Property { get; }

    public TimelineViewModel Timeline { get; }

    public TimelineLayerViewModel Layer { get; }

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

    public ReadOnlyReactivePropertySlim<double> ObserveHeight { get; }

    public event EventHandler<(double OldHeight, double NewHeight)>? HeightChanged;

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
