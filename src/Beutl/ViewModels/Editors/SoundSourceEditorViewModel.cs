using Beutl.Extensibility;
using Beutl.Media.Source;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class SoundSourceEditorViewModel : ValueEditorViewModel<ISoundSource?>
{
    public SoundSourceEditorViewModel(IAbstractProperty<ISoundSource?> property)
        : base(property)
    {
        ShortName = Value.Select(x => Path.GetFileName(x?.Name))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> ShortName { get; }
}
