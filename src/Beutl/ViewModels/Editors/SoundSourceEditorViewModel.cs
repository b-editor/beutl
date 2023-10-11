using Beutl.Animation;
using Beutl.Media.Source;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class SoundSourceEditorViewModel : ValueEditorViewModel<ISoundSource?>
{
    public SoundSourceEditorViewModel(IAbstractProperty<ISoundSource?> property)
        : base(property)
    {
        ShortName = Value.Select(x => Path.GetFileName(x?.Name))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> ShortName { get; }

    public void SetValueAndDispose(ISoundSource? oldValue, ISoundSource? newValue)
    {
        if (!EqualityComparer<ISoundSource?>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value != null)
            {
                CommandRecorder.Default.DoAndPush(new SetKeyFrameValueCommand(EditingKeyFrame.Value, oldValue, newValue));
            }
            else
            {
                CommandRecorder.Default.DoAndPush(new SetCommand(WrappedProperty, oldValue, newValue));
            }
        }
    }

    private sealed class SetKeyFrameValueCommand : IRecordableCommand
    {
        private readonly KeyFrame<ISoundSource?> _keyframe;
        private readonly string? _oldName;
        private readonly string? _newName;
        private ISoundSource? _oldValue;
        private ISoundSource? _newValue;

        public SetKeyFrameValueCommand(KeyFrame<ISoundSource?> setter, ISoundSource? oldValue, ISoundSource? newValue)
        {
            _keyframe = setter;
            _oldValue = oldValue;
            _newValue = newValue;
            _oldName = oldValue?.Name;
            _newName = newValue?.Name;
        }

        public void Do()
        {
            if (_newValue == null && _newName != null)
            {
                SoundSource.TryOpen(_newName, out SoundSource? newValue);
                _newValue = newValue;
            }

            _keyframe.SetValue(KeyFrame<ISoundSource?>.ValueProperty, _newValue);
            _oldValue?.Dispose();
            _oldValue = null;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            if (_oldValue == null && _oldName != null)
            {
                SoundSource.TryOpen(_oldName, out SoundSource? oldValue);
                _oldValue = oldValue;
            }

            _keyframe.SetValue(KeyFrame<ISoundSource?>.ValueProperty, _oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }

    private sealed class SetCommand : IRecordableCommand
    {
        private readonly IAbstractProperty<ISoundSource?> _setter;
        private readonly string? _oldName;
        private readonly string? _newName;
        private ISoundSource? _oldValue;
        private ISoundSource? _newValue;

        public SetCommand(IAbstractProperty<ISoundSource?> setter, ISoundSource? oldValue, ISoundSource? newValue)
        {
            _setter = setter;
            _oldValue = oldValue;
            _newValue = newValue;
            _oldName = oldValue?.Name;
            _newName = newValue?.Name;
        }

        public void Do()
        {
            if (_newValue == null && _newName != null)
            {
                SoundSource.TryOpen(_newName, out SoundSource? newValue);
                _newValue = newValue;
            }

            _setter.SetValue(_newValue);
            _oldValue?.Dispose();
            _oldValue = null;
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            if (_oldValue == null && _oldName != null)
            {
                SoundSource.TryOpen(_oldName, out SoundSource? oldValue);
                _oldValue = oldValue;
            }

            _setter.SetValue(_oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }
}
