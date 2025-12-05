using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Media.Source;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class VideoSourceEditorViewModel : ValueEditorViewModel<IVideoSource?>
{
    public VideoSourceEditorViewModel(IPropertyAdapter<IVideoSource?> property)
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

    public void SetValueAndDispose(IVideoSource? oldValue, IVideoSource? newValue)
    {
        if (!EqualityComparer<IVideoSource?>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder recorder = this.GetRequiredService<CommandRecorder>();
            if (EditingKeyFrame.Value != null)
            {
                recorder.DoAndPush(
                    new SetKeyFrameValueCommand(EditingKeyFrame.Value, oldValue, newValue, GetStorables()));
            }
            else
            {
                recorder.DoAndPush(
                    new SetCommand(PropertyAdapter, oldValue, newValue, GetStorables()));
            }
        }
    }

    private sealed class SetKeyFrameValueCommand(
        KeyFrame<IVideoSource?> setter,
        IVideoSource? oldValue,
        IVideoSource? newValue,
        ImmutableArray<CoreObject?> storables) : IRecordableCommand
    {
        private readonly Uri? _oldUri = oldValue?.Uri;
        private readonly Uri? _newUri = newValue?.Uri;
        private IVideoSource? _oldValue = oldValue;
        private IVideoSource? _newValue = newValue;

        public ImmutableArray<CoreObject?> GetStorables() => storables;

        public void Do()
        {
            if (_newValue == null && _newUri != null)
            {
                VideoSource.TryOpen(_newUri, out VideoSource? newValue);
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
            if (_oldValue == null && _oldUri != null)
            {
                VideoSource.TryOpen(_oldUri, out VideoSource? oldValue);
                _oldValue = oldValue;
            }

            setter.SetValue(KeyFrame<IVideoSource?>.ValueProperty, _oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }

    private sealed class SetCommand(
        IPropertyAdapter<IVideoSource?> setter,
        IVideoSource? oldValue,
        IVideoSource? newValue,
        ImmutableArray<CoreObject?> storables) : IRecordableCommand
    {
        private readonly Uri? _oldUri = oldValue?.Uri;
        private readonly Uri? _newUri = newValue?.Uri;
        private IVideoSource? _oldValue = oldValue;
        private IVideoSource? _newValue = newValue;

        public ImmutableArray<CoreObject?> GetStorables() => storables;

        public void Do()
        {
            if (_newValue == null && _newUri != null)
            {
                VideoSource.TryOpen(_newUri, out VideoSource? newValue);
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
                VideoSource.TryOpen(_oldUri, out VideoSource? oldValue);
                _oldValue = oldValue;
            }

            setter.SetValue(_oldValue);
            _newValue?.Dispose();
            _newValue = null;
        }
    }
}
