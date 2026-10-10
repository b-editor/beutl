using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.ProjectSystem;
using Dock.Model.Controls;

namespace Beutl.ViewModels;

public partial class EditViewModel
{
    // While set, an open property tab shows the selection where it is, so a layout being restored or applied
    // keeps the tabs it put in front.
    private bool _keepFrontTabs;

    // A layout change replaces the property tabs with empty ones, which show the selection only once it is
    // opened in them again.
    internal void OpenSelectionInPropertyTabs()
    {
        KeepingFrontTabs(() => OpenSelectionInPropertyTabs(_editorSelection.SelectedObject.Value));
    }

    private void KeepingFrontTabs(Action action)
    {
        _keepFrontTabs = true;
        try
        {
            action();
        }
        finally
        {
            _keepFrontTabs = false;
        }
    }

    // The property tabs show what they are opened with, as other tools do; selecting opens the selection in them.
    private void OpenSelectionInPropertyTabs(CoreObject? selected)
    {
        // The property tab follows the selection only while one is open; property editors open it.
        if (selected != null && FindToolTab<ObjectPropertyTabViewModel>() != null)
        {
            OpenInPropertyTab(
                ObjectPropertyTabViewModel.FindReusable(this, selected),
                () => new ObjectPropertyTabViewModel(this),
                tab => tab.NavigateCore(selected, false, null));
        }

        // The tool pickers leave the element property tab out, so selecting opens it even after it was closed.
        // It comes last, so it ends in front where both tools share a dock.
        if (selected is Element element)
        {
            OpenInPropertyTab(
                ElementPropertyTabViewModel.FindReusable(this, element),
                () => new ElementPropertyTabViewModel(this),
                tab => tab.Element.Value = element);
        }
    }

    private void OpenInPropertyTab<T>(T? reusable, Func<T> create, Action<T> show)
        where T : class, IToolContext
    {
        T tab = reusable ?? create();
        show(tab);
        if (reusable != null && _keepFrontTabs) return;

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
