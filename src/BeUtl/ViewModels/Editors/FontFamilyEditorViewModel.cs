using BeUtl.Media;
using BeUtl.Services.Editors.Wrappers;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Editors;

public sealed class FontFamilyEditorViewModel : BaseEditorViewModel<FontFamily>
{
    public FontFamilyEditorViewModel(IWrappedProperty<FontFamily> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<FontFamily> Value { get; }
}
