using Beutl.Animation;
using Beutl.Media.Source;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class VideoSourceEditorViewModel : ValueEditorViewModel<IVideoSource?>
{
    public VideoSourceEditorViewModel(IAbstractProperty<IVideoSource?> property)
        : base(property)
    {
        ShortName = Value.Select(x => Path.GetFileName(x?.Name))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> ShortName { get; }

    public void SetValueAndDispose(IVideoSource? oldValue, IVideoSource? newValue)
    {
        if (!EqualityComparer<IVideoSource?>.Default.Equals(oldValue, newValue))
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

    private sealed class SetKeyFrameValueCommand(KeyFrame<IVideoSource?> setter, IVideoSource? oldValue, IVideoSource? newValue) : IRecordableCommand
    {
        private readonly string? _oldName = oldValue?.Name;
        private readonly string? _newName = newValue?.Name;
        private IVideoSource? _oldValue = oldValue;
        private IVideoSource? _newValue = newValue;

        public void Do()
        {
            if (_newValue == null && _newName != null)
            {
                VideoSource.TryOpen(_newName, out VideoSource? newValue);
                _newValue = newValue;
            }

            setter.SetValue(KeyFrame<IVideoSource?>.ValueProperty, _newValue);
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
                VideoSource.TryOpen(_oldName, out VideoSource? oldValue);
                _oldValue = oldValue;
            }

            setter.SetValue(KeyFrame<IVideoSource?>.ValueProperty, _oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }

    private sealed class SetCommand(IAbstractProperty<IVideoSource?> setter, IVideoSource? oldValue, IVideoSource? newValue) : IRecordableCommand
    {
        private readonly string? _oldName = oldValue?.Name;
        private readonly string? _newName = newValue?.Name;
        private IVideoSource? _oldValue = oldValue;
        private IVideoSource? _newValue = newValue;

        public void Do()
        {
            if (_newValue == null && _newName != null)
            {
                VideoSource.TryOpen(_newName, out VideoSource? newValue);
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
                VideoSource.TryOpen(_oldName, out VideoSource? oldValue);
                _oldValue = oldValue;
            }

            setter.SetValue(_oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }
}
