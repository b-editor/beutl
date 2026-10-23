using Beutl.Configuration;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PropertyEditors.Views;

internal static class MediaSourceEditorHelper
{
    public static void MatchElementToOriginalDuration(PropertyEditorViewModel vm)
    {
        if (vm.GetService<Element>() is not { } element) return;

        // An open timeline also animates the clip to its new length.
        TimelineTabViewModel? timeline = vm.GetService<IEditorContext>()?.FindToolTab<TimelineTabViewModel>();
        if (timeline?.GetViewModelFor(element) is { } elmViewModel)
        {
            elmViewModel.ChangeToOriginalDuration.Execute();
            return;
        }

        if (element.FindHierarchicalParent<Scene>() is { } scene
            && vm.GetService<IElementResizeService>() is { } resizeService)
        {
            resizeService.ResizeToOriginalDuration(
                scene, element, GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled.Value);
        }
    }
}
