using Beutl.Editor.Components.SceneSettingsTab.ViewModels;

namespace Beutl.Editor.Components.Helpers;

internal static class SceneSettingsTabHelper
{
    public static void Show(IEditorContext editorContext)
    {
        if (editorContext.FindToolTab<SceneSettingsTabViewModel>() is { } tab)
        {
            tab.IsSelected.Value = true;
        }
        else
        {
            editorContext.OpenToolTab(new SceneSettingsTabViewModel(editorContext));
        }
    }
}
