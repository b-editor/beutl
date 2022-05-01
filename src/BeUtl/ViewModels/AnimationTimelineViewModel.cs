using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Avalonia;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

public sealed class AnimationTimelineViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public AnimationTimelineViewModel(Layer layer, IAnimatablePropertyInstance setter, EditorViewModelDescription description)
    {
        Layer = layer;
        Setter = setter;
        Description = description;
        Scene = Layer.FindRequiredLogicalParent<Scene>();

        ISubject<TimelineOptions> optionsSubject = Scene.GetSubject(Scene.TimelineOptionsProperty);

        BorderMargin = Layer.GetSubject(Layer.StartProperty)
            .CombineLatest(optionsSubject)
            .Select(item => new Thickness(item.First.ToPixel(item.Second.Scale), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Width = Layer.GetSubject(Layer.LengthProperty)
            .CombineLatest(optionsSubject)
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Color = Layer.GetSubject(Layer.AccentColorProperty)
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

    public Layer Layer { get; }

    public IAnimatablePropertyInstance Setter { get; }

    public EditorViewModelDescription Description { get; }

    public ReadOnlyReactivePropertySlim<Thickness> BorderMargin { get; }

    public ReadOnlyReactivePropertySlim<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> Color { get; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public ReactivePropertySlim<bool> IsSelected { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

    public void AddAnimation(Easing easing)
    {
        Type type = typeof(Animation<>).MakeGenericType(Setter.Property.PropertyType);

        if (Activator.CreateInstance(type) is IAnimation animation)
        {
            animation.Easing = easing;
            animation.Duration = TimeSpan.FromSeconds(2);
            object? value = Setter.Value;

            if (value != null)
            {
                animation.Previous = value;
                animation.Next = value;
            }

            Setter.AddChild(animation).DoAndRecord(CommandRecorder.Default);
        }
    }
}
