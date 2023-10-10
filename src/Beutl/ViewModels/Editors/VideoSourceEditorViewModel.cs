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

    private sealed class SetKeyFrameValueCommand : IRecordableCommand
    {
        private readonly KeyFrame<IVideoSource?> _keyframe;
        private readonly string? _oldName;
        private readonly string? _newName;
        private IVideoSource? _oldValue;
        private IVideoSource? _newValue;

        public SetKeyFrameValueCommand(KeyFrame<IVideoSource?> setter, IVideoSource? oldValue, IVideoSource? newValue)
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
                VideoSource.TryOpen(_newName, out VideoSource? newValue);
                _newValue = newValue;
            }

            _keyframe.SetValue(KeyFrame<IVideoSource?>.ValueProperty, _newValue);
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

            _keyframe.SetValue(KeyFrame<IVideoSource?>.ValueProperty, _oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }

    private sealed class SetCommand : IRecordableCommand
    {
        private readonly IAbstractProperty<IVideoSource?> _setter;
        private readonly string? _oldName;
        private readonly string? _newName;
        private IVideoSource? _oldValue;
        private IVideoSource? _newValue;

        public SetCommand(IAbstractProperty<IVideoSource?> setter, IVideoSource? oldValue, IVideoSource? newValue)
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
                VideoSource.TryOpen(_newName, out VideoSource? newValue);
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
                VideoSource.TryOpen(_oldName, out VideoSource? oldValue);
                _oldValue = oldValue;
            }

            _setter.SetValue(_oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }
}
