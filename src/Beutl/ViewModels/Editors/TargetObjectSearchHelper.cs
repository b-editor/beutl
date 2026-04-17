using Beutl.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.ViewModels.Editors;

internal static class TargetObjectSearchHelper
{
    public static IReadOnlyList<TargetObjectInfo> GetAvailableTargets<T>(IServiceProvider serviceProvider)
        where T : CoreObject
    {
        var scene = serviceProvider.GetService<EditViewModel>()?.Scene;
        if (scene == null) return [];

        var searcher = new ObjectSearcher(scene, obj =>
            obj is T && obj is not IPresenter<T>);

        return searcher.SearchAll()
            .Cast<T>()
            .Select(x => new TargetObjectInfo(
                CoreObjectHelper.GetDisplayName(x), x, CoreObjectHelper.GetOwnerElement(x)))
            .ToList();
    }
}
