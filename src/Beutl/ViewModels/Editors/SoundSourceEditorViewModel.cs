using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class SoundSourceEditorViewModel : ValueEditorViewModel<ISoundSource?>
{
    public SoundSourceEditorViewModel(IPropertyAdapter<ISoundSource?> property)
        : base(property)
    {
        FullName = Value.Select(x => x?.Uri.LocalPath)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        FileInfo = FullName.Select(p => p != null ? new FileInfo(p) : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> FullName { get; }

    public ReadOnlyReactivePropertySlim<FileInfo?> FileInfo { get; }

    public void SetValueAndDispose(ISoundSource? oldValue, ISoundSource? newValue)
    {
        if (!EqualityComparer<ISoundSource?>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value is { } kf)
            {
                // TODO: MediaSource.Openがされた状態ではUnmanagedなリソースを作成せず，EngineObject.Resourceが作成，更新されたときにリソースの作成などを行う
                kf.Value = newValue;
            }
            else
            {
                PropertyAdapter.SetValue(newValue);
            }

            Commit();
        }
    }
}
