using BeUtl.Media;
using BeUtl.ProjectSystem;

namespace BeUtl.ViewModels.Editors;

public sealed class FontFamilyEditorViewModel : BaseEditorViewModel<FontFamily>
{
    public FontFamilyEditorViewModel(Setter<FontFamily> setter)
        : base(setter)
    {
    }
}
