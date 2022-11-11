using Beutl.Framework;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class FontFamilyEditorViewModel : ValueEditorViewModel<FontFamily>
{
    public FontFamilyEditorViewModel(IAbstractProperty<FontFamily> property)
        : base(property)
    {
    }
}
