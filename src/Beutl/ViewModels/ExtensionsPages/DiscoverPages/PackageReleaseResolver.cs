using System.Reactive.Concurrency;

using Beutl.Api.Objects;
using NuGet.Packaging.Core;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

internal static class PackageReleaseResolver
{
    public static IObservable<Release?> ObserveLatest(
        IObservable<PackageIdentity?> requests,
        IScheduler stateScheduler,
        Func<Release?> getSelectedRelease,
        Func<IEnumerable<Release>> getAllReleases,
        Func<string, Task<Release>> getReleaseAsync,
        Action<Exception> onError)
    {
        return requests
            .ObserveOn(stateScheduler)
            .Select(id => CaptureState(
                id,
                getSelectedRelease,
                getAllReleases,
                onError))
            .Select(request => Observable.FromAsync(() => ResolveAsync(
                request,
                getReleaseAsync,
                onError)))
            .Switch()
            .ObserveOn(stateScheduler);
    }

    private static ResolutionRequest CaptureState(
        PackageIdentity? id,
        Func<Release?> getSelectedRelease,
        Func<IEnumerable<Release>> getAllReleases,
        Action<Exception> onError)
    {
        try
        {
            return new ResolutionRequest(
                id,
                getSelectedRelease(),
                [.. getAllReleases()]);
        }
        catch (Exception ex)
        {
            onError(ex);
            return new ResolutionRequest(null, null, []);
        }
    }

    private static async Task<Release?> ResolveAsync(
        ResolutionRequest request,
        Func<string, Task<Release>> getReleaseAsync,
        Action<Exception> onError)
    {
        try
        {
            if (request.InstalledPackage is null)
            {
                return null;
            }

            string version = request.InstalledPackage.Version.ToString();
            if (request.SelectedRelease is { } selected && selected.Version.Value == version)
            {
                return selected;
            }

            Release? cached = request.AllReleases.FirstOrDefault(x => x.Version.Value == version);
            return cached ?? await getReleaseAsync(version);
        }
        catch (Exception ex)
        {
            onError(ex);
            return null;
        }
    }

    private sealed record ResolutionRequest(
        PackageIdentity? InstalledPackage,
        Release? SelectedRelease,
        Release[] AllReleases);
}
