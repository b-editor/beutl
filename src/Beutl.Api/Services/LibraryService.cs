using System.Diagnostics;

using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public class LibraryService(BeutlApiApplication clients) : IBeutlApiResource
{
    public async Task<Package> GetPackage(string name)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackage", ActivityKind.Client);
        PackageResponse package = await clients.Packages.GetPackageAsync(name);
        Profile owner = await GetProfile(package.Owner.Name);

        return new Package(owner, package, clients);
    }

    public async Task<Profile> GetProfile(string name)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetProfile", ActivityKind.Client);
        ProfileResponse response = await clients.Users.GetUserAsync(name);
        return new Profile(response, clients);
    }

    public async Task<Package[]> GetPackages(int start = 0, int count = 30)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackages", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        return await (await clients.Library.GetLibraryAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Package.Name))
            .ToArrayAsync();
    }

    public async Task<Release> GetPackage(Package package)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackage", ActivityKind.Client);

        GotPackageResponse response = await clients.Library.GetPackageAsync(new GetPackageRequest(package.Id));
        if (response.Latest_release == null)
            throw new Exception("No release");

        return await package.GetReleaseAsync(response.Latest_release.Version);
    }

    public async Task RemovePackage(Package package)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.RemovePackage", ActivityKind.Client);

        (await clients.Library.DeletePackageAsync(package.Name)).Dispose();
    }
}
