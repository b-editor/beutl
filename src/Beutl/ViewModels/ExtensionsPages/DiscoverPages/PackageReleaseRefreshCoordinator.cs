using System.Reactive.Subjects;

using Beutl.Api.Objects;

namespace Beutl.ViewModels.ExtensionsPages.DiscoverPages;

internal static class PackageReleaseRefreshCoordinator
{
    private const int PageSize = 30;

    public static async Task RefreshAsync(
        BehaviorSubject<bool> releasesReady,
        Func<Task> refreshPackageAsync,
        Func<int, int, Task<Release[]>> getReleasesAsync,
        Action<Release[]> publishReleases)
    {
        bool wasReady = releasesReady.Value;
        releasesReady.OnNext(false);
        bool publicationStarted = false;

        try
        {
            await refreshPackageAsync();

            var releases = new List<Release>();
            Release[] page;
            do
            {
                page = await getReleasesAsync(releases.Count, PageSize);
                releases.AddRange(page);
            } while (page.Length == PageSize);

            publicationStarted = true;
            publishReleases([.. releases]);
            releasesReady.OnNext(true);
        }
        catch
        {
            if (!publicationStarted && wasReady)
            {
                releasesReady.OnNext(true);
            }

            throw;
        }
    }
}
