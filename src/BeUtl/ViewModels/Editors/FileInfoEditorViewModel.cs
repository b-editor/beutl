using Beutl.Framework;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class FileInfoEditorViewModel : BaseEditorViewModel<FileInfo>
{
    public FileInfoEditorViewModel(IAbstractProperty<FileInfo> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<FileInfo?> Value { get; }
}
