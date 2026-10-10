using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.ProjectSystem;
using Dock.Model.Controls;

namespace Beutl.ViewModels;

public partial class EditViewModel
{
    // The property tabs show what they are opened with, as other tools do; selecting opens the selection in them.
    private void OpenSelectionInPropertyTabs(CoreObject? selected)
    {
        // The tool pickers leave the element property tab out, so selecting opens it even after it was closed.
        if (selected is Element element)
        {
            OpenInPropertyTab(
                ElementPropertyTabViewModel.FindReusable(this, element),
                () => new ElementPropertyTabViewModel(this),
                tab => tab.Element.Value = element);
        }

        // The property tab follows the selection only while one is open; property editors open it.
        if (selected != null && FindToolTab<ObjectPropertyTabViewModel>() != null)
        {
            OpenInPropertyTab(
                ObjectPropertyTabViewModel.FindReusable(this, selected),
                () => new ObjectPropertyTabViewModel(this),
                tab => tab.NavigateCore(selected, false, null));
        }
    }

    private void OpenInPropertyTab<T>(T? reusable, Func<T> create, Action<T> show)
        where T : class, IToolContext
    {
        T tab = reusable ?? create();
        show(tab);

        // A new tab joins the open tabs of its kind, or, with none open, goes to its tool's own dock.
        IToolDock? dock = FindToolTab<T>() == null
            ? DockHost.Factory.GetAnchoredDock(tab.Extension.DefaultAnchor)
            : null;
        if (!DockHost.OpenToolTab(tab, dock) && reusable == null)
        {
            tab.Dispose();
        }
    }
}
