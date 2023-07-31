using Beutl.Framework;
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
}
