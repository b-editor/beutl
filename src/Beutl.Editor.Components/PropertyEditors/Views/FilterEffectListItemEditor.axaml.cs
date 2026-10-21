namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class FilterEffectListItemEditor : PresenterListItemEditor
{
    public FilterEffectListItemEditor()
    {
        InitializeComponent();
        AttachParts(reorderHandle, content, presenterEditor);
    }
}
