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
        TimelineTabViewModel? timeline = vm.GetService<EditViewModel>()?.FindToolTab<TimelineTabViewModel>();
        ElementViewModel? elmViewModel = timeline?.GetViewModelFor(element);

        elmViewModel?.ChangeToOriginalDuration.Execute();
    }
}
