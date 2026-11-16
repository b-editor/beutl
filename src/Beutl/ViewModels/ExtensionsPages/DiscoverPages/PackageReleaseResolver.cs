using System.Reactive.Concurrency;

using Beutl.Api.Objects;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

internal static class PackageReleaseResolver
{
    public static IObservable<PackageIdentity?> ObserveWhenReleasesReady(
        IObservable<PackageIdentity?> installedPackages,
        IObservable<bool> releasesReady,
        IScheduler stateScheduler)
    {
        return installedPackages
            .ObserveOn(stateScheduler)
            .CombineLatest(
                releasesReady.ObserveOn(stateScheduler),
                (installedPackage, isReady) => (InstalledPackage: installedPackage, IsReady: isReady))
            .Where(x => x.IsReady)
            .Select(x => x.InstalledPackage);
    }

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

            NuGetVersion installedVersion = request.InstalledPackage.Version;
            string version = installedVersion.ToString();
            if (request.SelectedRelease is { } selected && MatchesVersion(selected, installedVersion))
            {
                return selected;
            }

            Release? cached = request.AllReleases.FirstOrDefault(x => MatchesVersion(x, installedVersion));
            return cached ?? await getReleaseAsync(version);
        }
        catch (Exception ex)
        {
            onError(ex);
            return null;
        }
    }

    private static bool MatchesVersion(Release release, NuGetVersion installedVersion)
    {
        return NuGetVersion.TryParse(release.Version.Value, out NuGetVersion? releaseVersion)
            && VersionComparer.VersionRelease.Equals(releaseVersion, installedVersion);
    }

    private sealed record ResolutionRequest(
        PackageIdentity? InstalledPackage,
        Release? SelectedRelease,
        Release[] AllReleases);
}
