using System.Collections.Immutable;

using Beutl.Animation;
using Beutl.Media.Source;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class ImageSourceEditorViewModel : ValueEditorViewModel<IImageSource?>
{
    public ImageSourceEditorViewModel(IPropertyAdapter<IImageSource?> property)
        : base(property)
    {
        FullName = Value.Select(x => x != null ? Uri.UnescapeDataString(x.Uri.LocalPath) : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        FileInfo = FullName.Select(p => p != null ? new FileInfo(p) : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> FullName { get; }

    public ReadOnlyReactivePropertySlim<FileInfo?> FileInfo { get; }

    public void SetValueAndDispose(IImageSource? oldValue, IImageSource? newValue)
    {
        if (!EqualityComparer<IImageSource?>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            if (EditingKeyFrame.Value is { } kf)
            {
                recorder.DoAndPush(
                    new SetKeyFrameValueCommand(kf, oldValue, newValue, GetStorables()));
            }
            else
            {
                recorder.DoAndPush(
                    new SetCommand(PropertyAdapter, oldValue, newValue, GetStorables()));
            }
        }
    }

    private sealed class SetKeyFrameValueCommand(
        KeyFrame<IImageSource?> setter, IImageSource? oldValue, IImageSource? newValue,
        ImmutableArray<CoreObject?> storables) : IRecordableCommand
    {
        private readonly Uri? _oldUri = oldValue?.Uri;
        private readonly Uri? _newUri = newValue?.Uri;
        private IImageSource? _oldValue = oldValue;
        private IImageSource? _newValue = newValue;

        public ImmutableArray<CoreObject?> GetStorables() => storables;

        public void Do()
        {
            if (_newValue == null && _newUri != null)
            {
                BitmapSource.TryOpen(_newUri, out BitmapSource? newValue);
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
            if (_oldValue == null && _oldUri != null)
            {
                BitmapSource.TryOpen(_oldUri, out BitmapSource? oldValue);
                _oldValue = oldValue;
            }

            setter.SetValue(KeyFrame<IImageSource?>.ValueProperty, _oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }

    private sealed class SetCommand(
        IPropertyAdapter<IImageSource?> setter, IImageSource? oldValue, IImageSource? newValue,
        ImmutableArray<CoreObject?> storables) : IRecordableCommand
    {
        private readonly Uri? _oldUri = oldValue?.Uri;
        private readonly Uri? _newUri = newValue?.Uri;
        private IImageSource? _oldValue = oldValue;
        private IImageSource? _newValue = newValue;

        public ImmutableArray<CoreObject?> GetStorables() => storables;

        public void Do()
        {
            if (_newValue == null && _newUri != null)
            {
                BitmapSource.TryOpen(_newUri, out BitmapSource? newValue);
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
            if (_oldValue == null && _oldUri != null)
            {
                BitmapSource.TryOpen(_oldUri, out BitmapSource? oldValue);
                _oldValue = oldValue;
            }

            setter.SetValue(_oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }
}
