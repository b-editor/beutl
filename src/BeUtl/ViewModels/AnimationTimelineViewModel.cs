using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Avalonia;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Framework;
using BeUtl.ProjectSystem;
using BeUtl.Services.PrimitiveImpls;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels;

public sealed class AnimationTimelineViewModel : IDisposable, IToolContext
{
    private readonly CompositeDisposable _disposables = new();

    public AnimationTimelineViewModel(
        Layer layer,
        IAnimatablePropertyInstance setter,
        EditorViewModelDescription description,
        ITimelineOptionsProvider optionsProvider)
    {
        Layer = layer;
        Setter = setter;
        Description = description;
        OptionsProvider = optionsProvider;
        Scene = Layer.FindRequiredLogicalParent<Scene>();

        BorderMargin = Layer.GetSubject(Layer.StartProperty)
            .CombineLatest(OptionsProvider.Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Width = Layer.GetSubject(Layer.LengthProperty)
            .CombineLatest(OptionsProvider.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Color = Layer.GetSubject(Layer.AccentColorProperty)
            .Select(c => c.ToAvalonia())
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        PanelWidth = Scene.GetObservable(Scene.DurationProperty)
            .CombineLatest(OptionsProvider.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        SeekBarMargin = Scene.GetObservable(Scene.CurrentFrameProperty)
            .CombineLatest(OptionsProvider.Scale)
            .Select(item => new Thickness(item.First.ToPixel(item.Second), 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        EndingBarMargin = PanelWidth.Select(p => new Thickness(p, 0, 0, 0))
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        Header = layer.GetObservable(CoreObject.NameProperty)
            .Select(n => $"{n} / {Setter.Property.Name}")
            .ToReadOnlyReactivePropertySlim($"{layer.Name} / {Setter.Property.Name}")
            .AddTo(_disposables);
    }

    public Scene Scene { get; }

    public Layer Layer { get; }

    public IAnimatablePropertyInstance Setter { get; }

    public EditorViewModelDescription Description { get; }

    public ITimelineOptionsProvider OptionsProvider { get; }

    public ReadOnlyReactivePropertySlim<Thickness> BorderMargin { get; }

    public ReadOnlyReactivePropertySlim<double> Width { get; }

    public ReadOnlyReactivePropertySlim<Avalonia.Media.Color> Color { get; }

    public ReadOnlyReactivePropertySlim<double> PanelWidth { get; }

    public ReadOnlyReactivePropertySlim<Thickness> SeekBarMargin { get; }

    public ReadOnlyReactivePropertySlim<Thickness> EndingBarMargin { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public ToolTabExtension Extension => AnimationTimelineTabExtension.Instance;

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Bottom;

    public void Dispose()
    {
        _disposables.Dispose();
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
