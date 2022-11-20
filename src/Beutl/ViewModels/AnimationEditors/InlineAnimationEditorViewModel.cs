using System.Collections;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Commands;
using Beutl.Framework;
using Beutl.ProjectSystem;
using Beutl.Services;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.AnimationEditors;

public sealed class InlineAnimationEditorViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly InlineAnimationLayerViewModel _parent;

    public InlineAnimationEditorViewModel(
        IAnimationSpan animationSpan,
        InlineAnimationLayerViewModel parent)
    {
        Model = animationSpan;
        _parent = parent;
        Timeline = _parent.Timeline;
        Property = _parent.Property;

        Width = animationSpan.GetObservable(AnimationSpan.DurationProperty)
            .CombineLatest(Timeline.Options)
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReactiveProperty()
            .AddTo(_disposables);

        Width.Subscribe(w => animationSpan.Duration = w.ToTimeSpan(Timeline.Options.Value.Scale))
            .AddTo(_disposables);

        RemoveCommand.Subscribe(() =>
            {
                if (Property.Animation.Children is IList list)
                {
                    list.BeginRecord()
                        .Remove(Model)
                        .ToCommand()
                        .DoAndRecord(CommandRecorder.Default);
                }
            })
            .AddTo(_disposables);

        Header = PropertyEditorService.GetPropertyName(Property.Property);
    }

    ~InlineAnimationEditorViewModel()
    {
        Dispose();
    }

    public IAnimationSpan Model { get; }

    public IAnimation Animation => Property.Animation;

    public IAbstractAnimatableProperty Property { get; }

    public string Header { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveCommand RemoveCommand { get; } = new();

    public TimelineViewModel Timeline { get; }

    public ReactivePropertySlim<bool> ShowAnimationVisual => _parent.ShowAnimationVisual;

    public void SetDuration(TimeSpan old, TimeSpan @new)
    {
        if (Timeline.Scene.Parent is Project proj)
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
        if (Property.Animation.Children is IList list)
        {
            list.BeginRecord()
                .Move(oldIndex, newIndex)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void InsertForward(Easing easing)
    {
        if (Property.Animation.Children is IList list)
        {
            int index = list.IndexOf(Model);

            IAnimationSpan item = Property.CreateSpan(easing);
            list.BeginRecord()
                .Insert(index, item)
                .ToCommand()
                .DoAndRecord(CommandRecorder.Default);
        }
    }

    public void InsertBackward(Easing easing)
    {
        if (Property.Animation.Children is IList list)
        {
            int index = list.IndexOf(Model);

            IAnimationSpan item = Property.CreateSpan(easing);
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
