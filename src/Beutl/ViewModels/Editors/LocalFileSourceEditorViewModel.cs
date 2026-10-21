using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Engine;
using Beutl.IO;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

// The editors of a value read from a local file: an image, a video, a sound, a model or a LUT.
public abstract class LocalFileSourceEditorViewModel<T> : ValueEditorViewModel<T?>
    where T : EngineObject, IFileSource
{
    protected LocalFileSourceEditorViewModel(IPropertyAdapter<T?> property)
        : base(property)
    {
        FileInfo = Value.ObserveLocalPath()
            .DistinctUntilChanged()
            .Select(p => p != null ? new FileInfo(p) : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<FileInfo?> FileInfo { get; }
}
