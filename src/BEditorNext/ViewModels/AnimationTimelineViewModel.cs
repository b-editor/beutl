using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

using Avalonia;

using BEditorNext.ProjectSystem;
using BEditorNext.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels;

public sealed class AnimationTimelineViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public AnimationTimelineViewModel(SceneLayer layer, IAnimatableSetter setter, BaseEditorViewModel editorViewModel)
    {
        Layer = layer;
        Setter = setter;
        EditorViewModel = editorViewModel;
        Scene = Layer.FindRequiredLogicalParent<Scene>();

        ISubject<TimelineOptions> optionsSubject = Scene.GetSubject(Scene.TimelineOptionsProperty);

        BorderMargin = Layer.GetSubject(SceneLayer.StartProperty)
            .CombineLatest(optionsSubject)
            .Select(item => new Thickness(item.First.ToPixel(item.Second.Scale), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Width = Layer.GetSubject(SceneLayer.LengthProperty)
            .CombineLatest(optionsSubject)
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Color = Layer.GetSubject(SceneLayer.AccentColorProperty)
            .Select(c => c.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        PanelWidth = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(Scene.GetObservable(Scene.TimelineOptionsProperty))
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        SeekBarMargin = Scene.GetObservable(Scene.CurrentFrameProperty)
            .CombineLatest(Scene.GetObservable(Scene.TimelineOptionsProperty))
            .Select(item => new Thickness(item.First.ToPixel(item.Second.Scale), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        EndingBarMargin = PanelWidth.Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
    }

    ~AnimationTimelineViewModel()
    {
        _disposables.Dispose();
    }

    public Scene Scene { get; }

    public SceneLayer Layer { get; }

    public IAnimatableSetter Setter { get; }

    public BaseEditorViewModel EditorViewModel { get; }

    public ReadOnlyReactivePropertySlim<Thickness> BorderMargin { get; }

    public ReadOnlyReactivePropertySlim<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> Color { get; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

}
