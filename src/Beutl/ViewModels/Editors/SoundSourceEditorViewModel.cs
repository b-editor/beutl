using Beutl.Framework;
using Beutl.Media.Source;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class SoundSourceEditorViewModel : BaseEditorViewModel<ISoundSource?>
{
    public SoundSourceEditorViewModel(IAbstractProperty<ISoundSource?> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ShortName = Value.Select(x => Path.GetFileName(x?.Name))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<ISoundSource?> Value { get; }

    public ReadOnlyReactivePropertySlim<string?> ShortName { get; }
}
