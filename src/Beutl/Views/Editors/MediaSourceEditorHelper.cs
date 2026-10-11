using Beutl.Configuration;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

internal static class MediaSourceEditorHelper
{
    // 動画の長さに要素の長さを合わせる
    public static void MatchElementToOriginalDuration(BaseEditorViewModel vm)
    {
        if (vm.GetService<Element>() is not { } element) return;

        // An open timeline also animates the clip to its new length.
        TimelineTabViewModel? timeline = vm.GetService<EditViewModel>()?.FindToolTab<TimelineTabViewModel>();
        if (timeline?.GetViewModelFor(element) is { } elmViewModel)
        {
            elmViewModel.ChangeToOriginalDuration.Execute();
            return;
        }

        if (element.FindHierarchicalParent<Scene>() is { } scene
            && vm.GetService<IElementResizeService>() is { } resizeService)
        {
            resizeService.ResizeToOriginalDuration(
                scene, element, GlobalConfiguration.Instance.EditorConfig.IsRippleEnabled);
        }
    }
}
