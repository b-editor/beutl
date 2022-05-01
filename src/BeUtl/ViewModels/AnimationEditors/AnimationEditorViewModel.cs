using System.Collections;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using BeUtl.Animation;
using BeUtl.Animation.Easings;
using BeUtl.Commands;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.AnimationEditors;

public abstract class AnimationEditorViewModel : IDisposable
{
    protected CompositeDisposable Disposables = new();
    private bool _disposedValue;

    protected AnimationEditorViewModel(IAnimation animation, EditorViewModelDescription description)
    {
        Animation = animation;
        Description = description;

        Scene = description.PropertyInstance.FindRequiredLogicalParent<Scene>();
        ISubject<TimelineOptions> optionsSubject = Scene.GetSubject(Scene.TimelineOptionsProperty);

        Width = animation.GetSubject(BaseAnimation.DurationProperty)
            .CombineLatest(optionsSubject)
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReactiveProperty()
            .AddTo(Disposables);

        Width.Subscribe(w => animation.Duration = w.ToTimeSpan(Scene.TimelineOptions.Scale)).AddTo(Disposables);

        RemoveCommand.Subscribe(() => Setter.RemoveChild(Animation).DoAndRecord(CommandRecorder.Default)).AddTo(Disposables);

        IOperationPropertyMetadata metadata = Setter.Property.GetMetadata<IOperationPropertyMetadata>(Setter.Parent.GetType());
        Header = metadata.Header.ToObservable(Setter.Property.Name)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    ~AnimationEditorViewModel()
    {
        if (!_disposedValue)
            Dispose(false);
    }

    public IAnimation Animation { get; }

    public IAnimatablePropertyInstance Setter => (IAnimatablePropertyInstance)Description.PropertyInstance;

    public EditorViewModelDescription Description { get; }

    public bool CanReset => Setter.GetDefaultValue() != null;

    public ReadOnlyReactivePropertySlim<string?> Header { get; }

    public ReactiveProperty<double> Width { get; }

    public ReactiveCommand RemoveCommand { get; } = new();

    public Scene Scene { get; }

    public void SetDuration(TimeSpan old, TimeSpan @new)
    {
        if (Scene.Parent is Project proj)
        {
            @new = @new.RoundToRate(proj.FrameRate);
        }

        CommandRecorder.Default.DoAndPush(
            new ChangePropertyCommand<TimeSpan>(Animation, BaseAnimation.DurationProperty, @new, old));
    }

    public void SetEasing(Easing old, Easing @new)
    {
        CommandRecorder.Default.DoAndPush(new ChangePropertyCommand<Easing>(Animation, BaseAnimation.EasingProperty, @new, old));
    }

    public void Move(int newIndex, int oldIndex)
    {
        if (Setter.Children is IList list)
        {
            CommandRecorder.Default.PushOnly(new MoveCommand(list, newIndex, oldIndex));
        }
    }

    public void InsertForward(Easing easing)
    {
        if (Setter.Children is IList list)
        {
            int index = list.IndexOf(Animation);
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

                Setter.InsertChild(index, animation).DoAndRecord(CommandRecorder.Default);
            }
        }
    }

    public void InsertBackward(Easing easing)
    {
        if (Setter.Children is IList list)
        {
            int index = list.IndexOf(Animation);
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

                Setter.InsertChild(index + 1, animation).DoAndRecord(CommandRecorder.Default);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposedValue)
        {
            Dispose(true);
            _disposedValue = true;
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        Disposables.Dispose();
    }
}

public class AnimationEditorViewModel<T> : AnimationEditorViewModel
    where T : struct
{
    public AnimationEditorViewModel(Animation<T> animation, EditorViewModelDescription description)
        : base(animation, description)
    {
    }

    internal AnimationEditorViewModel(IAnimation animation, EditorViewModelDescription description)
        : base(animation, description)
    {
    }

    public new Animation<T> Animation => (Animation<T>)base.Animation;

    public new AnimatablePropertyInstance<T> Setter => (AnimatablePropertyInstance<T>)base.Setter;

    public void ResetPrevious()
    {
        object? defaultValue = Setter.GetDefaultValue();
        if (defaultValue != null)
        {
            SetPrevious(Setter.Value, (T)defaultValue);
        }
    }

    public void ResetNext()
    {
        object? defaultValue = Setter.GetDefaultValue();
        if (defaultValue != null)
        {
            SetNext(Setter.Value, (T)defaultValue);
        }
    }

    public void SetPrevious(T oldValue, T newValue)
    {
        if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder.Default.DoAndPush(new ChangePropertyCommand<T>(Animation, Animation<T>.PreviousProperty, newValue, oldValue));
        }
    }

    public void SetNext(T oldValue, T newValue)
    {
        if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder.Default.DoAndPush(new ChangePropertyCommand<T>(Animation, Animation<T>.NextProperty, newValue, oldValue));
        }
    }
}
