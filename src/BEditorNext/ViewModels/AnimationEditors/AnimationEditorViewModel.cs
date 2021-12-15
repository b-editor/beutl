using System.Collections;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;

using BEditorNext.Animation;
using BEditorNext.ProjectSystem;
using BEditorNext.ViewModels.Editors;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.AnimationEditors;

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
    }

    ~AnimationEditorViewModel()
    {
        if (!_disposedValue)
            Dispose(false);
    }

    public IAnimation Animation { get; }

    public IAnimatableSetter Setter => (IAnimatableSetter)EditorViewModel.Setter;

    public BaseEditorViewModel EditorViewModel { get; }

    public bool CanReset => Setter.Property.MetaTable.ContainsKey(PropertyMetaTableKeys.DefaultValue);

    public ReadOnlyReactivePropertySlim<string?> Header => EditorViewModel.Header;

    public ReactiveProperty<double> Width { get; }

    public Scene Scene { get; }

    public void SetDuration(TimeSpan old, TimeSpan @new)
    {
        CommandRecorder.Default.PushOnly(new SetDurationCommand(Animation, @new, old));
    }

    public void Move(int newIndex, int oldIndex)
    {
        if (Setter.Children is IList list)
        {
            CommandRecorder.Default.PushOnly(new MoveAnimationCommand(list, newIndex, oldIndex));
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

    private sealed class SetDurationCommand : IRecordableCommand
    {
        private readonly IAnimation _animation;
        private readonly TimeSpan _newDuration;
        private readonly TimeSpan _oldDuration;

        public SetDurationCommand(IAnimation animation, TimeSpan newDuration, TimeSpan oldDuration)
        {
            _animation = animation;
            _newDuration = newDuration;
            _oldDuration = oldDuration;
        }

        public void Do()
        {
            _animation.Duration = _newDuration;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _animation.Duration = _oldDuration;
        }
    }

    private sealed class MoveAnimationCommand : IRecordableCommand
    {
        private readonly IList _list;
        private readonly int _newIndex;
        private readonly int _oldIndex;

        public MoveAnimationCommand(IList list, int newIndex, int oldIndex)
        {
            _list = list;
            _newIndex = newIndex;
            _oldIndex = oldIndex;
        }

        public void Do()
        {
            object? item = _list[_oldIndex];
            _list.RemoveAt(_oldIndex);
            _list.Insert(_newIndex, item);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            object? item = _list[_newIndex];
            _list.RemoveAt(_newIndex);
            _list.Insert(_oldIndex, item);
        }
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
        if (CanReset)
        {
            SetPrevious(Setter.Value, Setter.Property.GetDefaultValue());
        }
    }

    public void ResetNext()
    {
        if (CanReset)
        {
            SetNext(Setter.Value, Setter.Property.GetDefaultValue());
        }
    }

    public void SetPrevious(T oldValue, T newValue)
    {
        CommandRecorder.Default.DoAndPush(new SetPreviousCommand(Animation, oldValue, newValue));
    }

    public void SetNext(T oldValue, T newValue)
    {
        CommandRecorder.Default.DoAndPush(new SetNextCommand(Animation, oldValue, newValue));
    }

    private sealed class SetPreviousCommand : IRecordableCommand
    {
        private readonly Animation<T> _animation;
        private readonly T _oldValue;
        private readonly T _newValue;

        public SetPreviousCommand(Animation<T> animation, T oldValue, T newValue)
        {
            _animation = animation;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _animation.Previous = _newValue;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _animation.Previous = _oldValue;
        }
    }

    private sealed class SetNextCommand : IRecordableCommand
    {
        private readonly Animation<T> _animation;
        private readonly T _oldValue;
        private readonly T _newValue;

        public SetNextCommand(Animation<T> animation, T oldValue, T newValue)
        {
            _animation = animation;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _animation.Next = _newValue;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _animation.Next = _oldValue;
        }
    }
}
