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

    private sealed class SetKeyFrameValueCommand(KeyFrame<ISoundSource?> setter, ISoundSource? oldValue, ISoundSource? newValue) : IRecordableCommand
    {
        private readonly string? _oldName = oldValue?.Name;
        private readonly string? _newName = newValue?.Name;
        private ISoundSource? _oldValue = oldValue;
        private ISoundSource? _newValue = newValue;

        public void Do()
        {
            if (_newValue == null && _newName != null)
            {
                SoundSource.TryOpen(_newName, out SoundSource? newValue);
                _newValue = newValue;
            }

            setter.SetValue(KeyFrame<ISoundSource?>.ValueProperty, _newValue);
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

            setter.SetValue(KeyFrame<ISoundSource?>.ValueProperty, _oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }

    private sealed class SetCommand(IAbstractProperty<ISoundSource?> setter, ISoundSource? oldValue, ISoundSource? newValue) : IRecordableCommand
    {
        private readonly string? _oldName = oldValue?.Name;
        private readonly string? _newName = newValue?.Name;
        private ISoundSource? _oldValue = oldValue;
        private ISoundSource? _newValue = newValue;

        public void Do()
        {
            if (_newValue == null && _newName != null)
            {
                SoundSource.TryOpen(_newName, out SoundSource? newValue);
                _newValue = newValue;
            }

            setter.SetValue(_newValue);
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

            setter.SetValue(_oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }
}
