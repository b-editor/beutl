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

    private sealed class SetKeyFrameValueCommand(KeyFrame<IImageSource?> setter, IImageSource? oldValue, IImageSource? newValue) : IRecordableCommand
    {
        private readonly string? _oldName = oldValue?.Name;
        private readonly string? _newName = newValue?.Name;
        private IImageSource? _oldValue = oldValue;
        private IImageSource? _newValue = newValue;

        public void Do()
        {
            if (_newValue == null && _newName != null)
            {
                BitmapSource.TryOpen(_newName, out BitmapSource? newValue);
                _newValue = newValue;
            }

            setter.SetValue(KeyFrame<IImageSource?>.ValueProperty, _newValue);
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

            setter.SetValue(KeyFrame<IImageSource?>.ValueProperty, _oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }

    private sealed class SetCommand(IAbstractProperty<IImageSource?> setter, IImageSource? oldValue, IImageSource? newValue) : IRecordableCommand
    {
        private readonly string? _oldName = oldValue?.Name;
        private readonly string? _newName = newValue?.Name;
        private IImageSource? _oldValue = oldValue;
        private IImageSource? _newValue = newValue;

        public void Do()
        {
            if (_newValue == null && _newName != null)
            {
                BitmapSource.TryOpen(_newName, out BitmapSource? newValue);
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
                BitmapSource.TryOpen(_oldName, out BitmapSource? oldValue);
                _oldValue = oldValue;
            }

            setter.SetValue(_oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }
}
