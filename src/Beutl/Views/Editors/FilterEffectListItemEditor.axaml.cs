namespace Beutl.Views.Editors;

public partial class FilterEffectListItemEditor : PresenterListItemEditor
{
    public FilterEffectListItemEditor()
    {
        InitializeComponent();
        AttachParts(reorderHandle, content, presenterEditor);
    }
}
