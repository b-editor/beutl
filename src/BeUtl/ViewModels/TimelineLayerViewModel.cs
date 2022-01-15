using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Media;

using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

public class TimelineLayerViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public TimelineLayerViewModel(Layer sceneLayer)
    {
        Model = sceneLayer;
        ISubject<TimelineOptions> optionsSubject = Scene.GetSubject(Scene.TimelineOptionsProperty);

        Margin = sceneLayer.GetSubject(Layer.ZIndexProperty)
            .Select(item => new Thickness(0, item.ToLayerPixel(), 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        BorderMargin = sceneLayer.GetSubject(Layer.StartProperty)
            .CombineLatest(optionsSubject)
            .Select(item => new Thickness(item.First.ToPixel(item.Second.Scale), 0, 0, 0))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width = sceneLayer.GetSubject(Layer.LengthProperty)
            .CombineLatest(optionsSubject)
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Color = sceneLayer.GetSubject(Layer.AccentColorProperty)
            .Select(c => c.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        ColorSetter = Color.Select(i => (FluentAvalonia.UI.Media.Color2)i)
            .ToReactiveProperty()
            .AddTo(_disposables);

        Remove.Subscribe(() => Scene.RemoveChild(Model, CommandRecorder.Default));

        ColorSetter.Subscribe(c => Model.AccentColor = Media.Color.FromArgb(c.A, c.R, c.G, c.B))
            .AddTo(_disposables);
    }

    ~TimelineLayerViewModel()
    {
        _disposables.Dispose();
    }

    public Layer Model { get; }

    public Scene Scene => (Scene)Model.Parent!;

    public ReactiveProperty<Thickness> Margin { get; }

    public ReactiveProperty<Thickness> BorderMargin { get; }

    public ReactiveProperty<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> Color { get; }

    public ReactiveProperty<FluentAvalonia.UI.Media.Color2> ColorSetter { get; }

    public ReactiveCommand Remove { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

    public void SyncModelToViewModel()
    {
        float scale = Scene.TimelineOptions.Scale;
        Model.UpdateTime(BorderMargin.Value.Left.ToTimeSpan(scale), Width.Value.ToTimeSpan(scale), CommandRecorder.Default);

        int layerNum = Margin.Value.ToLayerNumber();
        Scene.MoveChild(layerNum, Model, CommandRecorder.Default);

        Margin.Value = new Thickness(0, layerNum.ToLayerPixel(), 0, 0);
    }
}
