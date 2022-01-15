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

    protected AnimationEditorViewModel(IAnimation animation, BaseEditorViewModel editorViewModel)
    {
        Animation = animation;
        EditorViewModel = editorViewModel;

        Scene = animation.FindRequiredLogicalParent<Scene>();
        ISubject<TimelineOptions> optionsSubject = Scene.GetSubject(Scene.TimelineOptionsProperty);

        Width = animation.GetSubject(BaseAnimation.DurationProperty)
            .CombineLatest(optionsSubject)
            .Select(item => item.First.ToPixel(item.Second.Scale))
            .ToReactiveProperty()
            .AddTo(Disposables);

        Width.Subscribe(w => animation.Duration = w.ToTimeSpan(Scene.TimelineOptions.Scale)).AddTo(Disposables);

        RemoveCommand.Subscribe(() => Setter.RemoveChild(Animation, CommandRecorder.Default)).AddTo(Disposables);
    }

    ~AnimationEditorViewModel()
    {
        if (!_disposedValue)
            Dispose(false);
    }

    public IAnimation Animation { get; }

    public IAnimatableSetter Setter => (IAnimatableSetter)EditorViewModel.Setter;

    public BaseEditorViewModel EditorViewModel { get; }

    public bool CanReset => Setter.Property.GetMetadata(Setter.Parent.GetType()).DefaultValue != null;

    public ReadOnlyReactivePropertySlim<string?> Header => EditorViewModel.Header;

    public ReactiveProperty<double> Width { get; }

    public ReactiveCommand RemoveCommand { get; } = new();

    public Scene Scene { get; }

    public void SetDuration(TimeSpan old, TimeSpan @new)
    {
        CommandRecorder.Default.PushOnly(new ChangePropertyCommand<TimeSpan>(Animation, BaseAnimation.DurationProperty, @new, old));
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
            Type type = typeof(Animation<>).MakeGenericType(EditorViewModel.Setter.Property.PropertyType);

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

                Setter.InsertChild(index, animation, CommandRecorder.Default);
            }
        }
    }

    public void InsertBackward(Easing easing)
    {
        if (Setter.Children is IList list)
        {
            int index = list.IndexOf(Animation);
            Type type = typeof(Animation<>).MakeGenericType(EditorViewModel.Setter.Property.PropertyType);

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

                Setter.InsertChild(index + 1, animation, CommandRecorder.Default);
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
    public AnimationEditorViewModel(Animation<T> animation, BaseEditorViewModel<T> editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    internal AnimationEditorViewModel(IAnimation animation, BaseEditorViewModel editorViewModel)
        : base(animation, editorViewModel)
    {
    }

    public new Animation<T> Animation => (Animation<T>)base.Animation;

    public new AnimatableSetter<T> Setter => (AnimatableSetter<T>)base.Setter;

    public new BaseEditorViewModel<T> EditorViewModel => (BaseEditorViewModel<T>)base.EditorViewModel;

    public void ResetPrevious()
    {
        object? defaultValue = Setter.Property.GetMetadata(Setter.Parent.GetType()).DefaultValue;
        if (defaultValue != null)
        {
            SetPrevious(Setter.Value, (T)defaultValue);
        }
    }

    public void ResetNext()
    {
        object? defaultValue = Setter.Property.GetMetadata(Setter.Parent.GetType()).DefaultValue;
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
