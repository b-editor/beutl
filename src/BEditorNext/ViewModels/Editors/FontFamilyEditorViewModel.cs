using BEditorNext.Media;
using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels.Editors;

public sealed class FontFamilyEditorViewModel : BaseEditorViewModel<FontFamily>
{
    public FontFamilyEditorViewModel(Setter<FontFamily> setter)
        : base(setter)
    {
    }
}
