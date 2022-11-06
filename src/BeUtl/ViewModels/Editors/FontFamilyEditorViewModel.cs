using Beutl.Framework;
using Beutl.Media;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class FontFamilyEditorViewModel : BaseEditorViewModel<FontFamily>
{
    public FontFamilyEditorViewModel(IAbstractProperty<FontFamily> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<FontFamily> Value { get; }
}
