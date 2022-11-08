using System.Collections;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Commands;
using Beutl.Framework;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.Editors.Wrappers;
using Beutl.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.AnimationEditors;

public sealed class AnimationEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public AnimationEditorViewModel(
        IAnimationSpan animationSpan,
        IAbstractAnimatableProperty property,
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

        RemoveCommand.Subscribe(() =>
            {
                if (WrappedProperty.Animation.Children is IList list)
                {
                    list.BeginRecord()
                        .Remove(Model)
                        .ToCommand()
                        .DoAndRecord(CommandRecorder.Default);
                }
            })
            .AddTo(_disposables);

        Header = PropertyEditorService.GetPropertyName(property.Property);
    }

    ~AnimationEditorViewModel()
    {
        Dispose();
    }

    public IAnimationSpan Model { get; }

    public IAnimation Animation => WrappedProperty.Animation;

    public IAbstractAnimatableProperty WrappedProperty { get; }

    public string Header { get; }

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
        if (WrappedProperty.Animation.Children is IList list)
        {
            list.BeginRecord()
                .Move(oldIndex, newIndex)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void InsertForward(Easing easing)
    {
        if (WrappedProperty.Animation.Children is IList list)
        {
            int index = list.IndexOf(Model);

            IAnimationSpan item = WrappedProperty.CreateSpan(easing);
            list.BeginRecord()
                .Insert(index, item)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void InsertBackward(Easing easing)
    {
        if (WrappedProperty.Animation.Children is IList list)
        {
            int index = list.IndexOf(Model);

            IAnimationSpan item = WrappedProperty.CreateSpan(easing);
            list.BeginRecord()
                .Insert(index + 1, item)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
