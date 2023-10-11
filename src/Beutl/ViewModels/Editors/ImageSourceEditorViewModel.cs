using Beutl.Animation;
using Beutl.Media.Source;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class ImageSourceEditorViewModel : ValueEditorViewModel<IImageSource?>
{
    public ImageSourceEditorViewModel(IAbstractProperty<IImageSource?> property)
        : base(property)
    {
        ShortName = Value.Select(x => Path.GetFileName(x?.Name))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> ShortName { get; }

    public void SetValueAndDispose(IImageSource? oldValue, IImageSource? newValue)
    {
        if (!EqualityComparer<IImageSource?>.Default.Equals(oldValue, newValue))
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
        private readonly KeyFrame<IImageSource?> _keyframe;
        private readonly string? _oldName;
        private readonly string? _newName;
        private IImageSource? _oldValue;
        private IImageSource? _newValue;

        public SetKeyFrameValueCommand(KeyFrame<IImageSource?> setter, IImageSource? oldValue, IImageSource? newValue)
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
                BitmapSource.TryOpen(_newName, out BitmapSource? newValue);
                _newValue = newValue;
            }

            _keyframe.SetValue(KeyFrame<IImageSource?>.ValueProperty, _newValue);
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
                BitmapSource.TryOpen(_oldName, out BitmapSource? oldValue);
                _oldValue = oldValue;
            }

            _keyframe.SetValue(KeyFrame<IImageSource?>.ValueProperty, _oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }

    private sealed class SetCommand : IRecordableCommand
    {
        private readonly IAbstractProperty<IImageSource?> _setter;
        private readonly string? _oldName;
        private readonly string? _newName;
        private IImageSource? _oldValue;
        private IImageSource? _newValue;

        public SetCommand(IAbstractProperty<IImageSource?> setter, IImageSource? oldValue, IImageSource? newValue)
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
                BitmapSource.TryOpen(_newName, out BitmapSource? newValue);
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
                BitmapSource.TryOpen(_oldName, out BitmapSource? oldValue);
                _oldValue = oldValue;
            }

            _setter.SetValue(_oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }
}
