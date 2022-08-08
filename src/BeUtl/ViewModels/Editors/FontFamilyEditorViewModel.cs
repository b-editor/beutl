using BeUtl.Framework;
using BeUtl.Media;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

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
