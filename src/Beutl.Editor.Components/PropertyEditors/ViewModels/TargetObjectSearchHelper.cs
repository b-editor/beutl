using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

internal static class TargetObjectSearchHelper
{
    public static IReadOnlyList<TargetObjectInfo> GetAvailableTargets<T>(IServiceProvider serviceProvider)
        where T : CoreObject
    {
        var scene = serviceProvider.GetService<Scene>();
        if (scene == null) return [];

        var searcher = new ObjectSearcher(scene, obj =>
            obj is T && obj is not IPresenter<T>);

        return searcher.SearchAll()
            .Cast<T>()
            .Select(x => new TargetObjectInfo(CoreObjectHelper.GetDisplayName(x), x))
            .ToList();
    }
}
