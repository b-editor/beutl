using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.ViewModels.Tools;

namespace Beutl.ViewModels;

// Opens the transition tool on the transition the timeline asks for.
internal sealed class TransitionEditorService(EditViewModel editViewModel) : ITransitionEditorService
{
    public void Edit(Element element, ElementEdge edge)
    {
        TransitionTabViewModel tab = TransitionTabViewModel.FindReusable(editViewModel, element, edge)
                                     ?? new TransitionTabViewModel(editViewModel);
        tab.Show(element, edge);
        editViewModel.OpenToolTab(tab);
    }
}
