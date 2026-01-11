using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class SoundSourceEditorViewModel : ValueEditorViewModel<SoundSource?>
{
    public SoundSourceEditorViewModel(IPropertyAdapter<SoundSource?> property)
        : base(property)
    {
        FullName = Value.Select(x => x?.HasUri == true ? x.Uri.LocalPath : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        FileInfo = FullName.Select(p => p != null ? new FileInfo(p) : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> FullName { get; }

    public ReadOnlyReactivePropertySlim<FileInfo?> FileInfo { get; }

    public void SetValueAndDispose(SoundSource? oldValue, SoundSource? newValue)
    {
        if (!EqualityComparer<SoundSource?>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value is { } kf)
            {
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
