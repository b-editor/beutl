using BeUtl.Media;
using BeUtl.Services.Editors.Wrappers;

namespace BeUtl.ViewModels.Editors;

public sealed class FontFamilyEditorViewModel : BaseEditorViewModel<FontFamily>
{
    public FontFamilyEditorViewModel(IWrappedProperty<FontFamily> property)
        : base(property)
    {
    }
}
