using System.Collections;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Commands;
using BeUtl.ProjectSystem;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.AnimationEditors;

// Todo: AnimationSpanEditorViewModelとコードを共通化する。
public sealed class AnimationEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public AnimationEditorViewModel(
        IAnimationSpan animationSpan,
        IWrappedProperty.IAnimatable property,
        ITimelineOptionsProvider optionsProvider)
    {
        Model = animationSpan;
        OptionsProvider = optionsProvider;
        WrappedProperty = property;

        Width = animationSpan.GetObservable(AnimationSpan.DurationProperty)
            .CombineLatest(optionsProvider.Scale)
            .Select(item => item.First.ToPixel(item.Second))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width.Subscribe(w => animationSpan.Duration = w.ToTimeSpan(optionsProvider.Options.Value.Scale))
            .AddTo(_disposables);

        RemoveCommand.Subscribe(() => WrappedProperty.Remove(Model))
            .AddTo(_disposables);

        Header = property.Header
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
    }

    ~AnimationEditorViewModel()
    {
        Dispose();
    }

    public IAnimationSpan Model { get; }

    public IAnimation Animation => WrappedProperty.Animation;

    public IWrappedProperty.IAnimatable WrappedProperty { get; }

    public bool CanReset => WrappedProperty.GetDefaultValue() != null;

    public ReadOnlyReactivePropertySlim<string?> Header { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveCommand RemoveCommand { get; } = new();

    public ITimelineOptionsProvider OptionsProvider { get; }

    public void SetDuration(TimeSpan old, TimeSpan @new)
    {
        if (OptionsProvider.Scene.Parent is Project proj)
        {
            @new = @new.RoundToRate(proj.GetFrameRate());
        }

        new ChangePropertyCommand<TimeSpan>(Model, AnimationSpan.DurationProperty, @new, old)
            .DoAndRecord(CommandRecorder.Default);
    }

    public void SetEasing(Easing old, Easing @new)
    {
        new ChangePropertyCommand<Easing>(Model, AnimationSpan.EasingProperty, @new, old)
            .DoAndRecord(CommandRecorder.Default);
    }

    public void Move(int newIndex, int oldIndex)
    {
        WrappedProperty.Move(newIndex, oldIndex);
    }

    public void InsertForward(Easing easing)
    {
        int index = WrappedProperty.IndexOf(Model);

        IAnimationSpan item = WrappedProperty.CreateSpan(easing);
        WrappedProperty.Insert(index, item);
    }

    public void InsertBackward(Easing easing)
    {
        int index = WrappedProperty.IndexOf(Model);

        IAnimationSpan item = WrappedProperty.CreateSpan(easing);
        WrappedProperty.Insert(index + 1, item);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
