using Beutl.Api.Objects;
using NuGet.Packaging.Core;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

internal static class PackageReleaseResolver
{
    public static IObservable<Release?> ObserveLatest(
        IObservable<PackageIdentity?> requests,
        Func<Release?> getSelectedRelease,
        Func<IEnumerable<Release>> getAllReleases,
        Func<string, Task<Release>> getReleaseAsync,
        Action<Exception> onError)
    {
        return requests
            .Select(id => Observable.FromAsync(() => ResolveAsync(
                id,
                getSelectedRelease,
                getAllReleases,
                getReleaseAsync,
                onError)))
            .Switch();
    }

    private static async Task<Release?> ResolveAsync(
        PackageIdentity? id,
        Func<Release?> getSelectedRelease,
        Func<IEnumerable<Release>> getAllReleases,
        Func<string, Task<Release>> getReleaseAsync,
        Action<Exception> onError)
    {
        try
        {
            if (id is null)
            {
                return null;
            }

            string version = id.Version.ToString();
            if (getSelectedRelease() is { } selected && selected.Version.Value == version)
            {
                return selected;
            }

            Release? cached = getAllReleases().FirstOrDefault(x => x.Version.Value == version);
            return cached ?? await getReleaseAsync(version);
        }
        catch (Exception ex)
        {
            onError(ex);
            return null;
        }
    }
}
