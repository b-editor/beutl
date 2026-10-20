using Beutl.Editor.Components.TransitionTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.TransitionTab;

// Opens the transition tool on the transition the timeline asks for.
internal sealed class TransitionEditorService(IEditorContext editorContext) : ITransitionEditorService
{
    public void Edit(Element element, ElementEdge edge)
    {
        TransitionTabViewModel tab = TransitionTabViewModel.FindReusable(editorContext, element, edge)
                                     ?? new TransitionTabViewModel(editorContext);
        tab.Show(element, edge);
        editorContext.OpenToolTab(tab);
    }
}
