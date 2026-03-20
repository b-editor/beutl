using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class CubeSourceEditorViewModel : ValueEditorViewModel<CubeSource?>
{
    public CubeSourceEditorViewModel(IPropertyAdapter<CubeSource?> property)
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

    public void SetValueAndCommit(CubeSource? oldValue, CubeSource? newValue)
    {
        if (!EqualityComparer<CubeSource?>.Default.Equals(oldValue, newValue))
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
