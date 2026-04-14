using Beutl.Graphics3D.Models;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class ModelSourceEditorViewModel : ValueEditorViewModel<ModelSource?>
{
    public ModelSourceEditorViewModel(IPropertyAdapter<ModelSource?> property)
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
}
