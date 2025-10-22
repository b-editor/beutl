using System.Diagnostics;
using System.Reactive.Linq;
using Beutl.Api.Clients;
using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public class LibraryService(BeutlApiApplication clients) : IBeutlApiResource
{
    public async Task<Package> GetPackage(string name)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackage", ActivityKind.Client);
        PackageResponse package = await clients.Packages.GetPackage(name);
        var owner = new Profile(package.Owner, clients);

        return new Package(owner, package, clients);
    }

    public async Task<Profile> GetProfile(string name)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetProfile", ActivityKind.Client);
        ProfileResponse response = await clients.Users.GetUser(name);
        return new Profile(response, clients);
    }

    public async Task<Package[]> GetPackages(int start = 0, int count = 30)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackages", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        // TODO: System.Interactive.AsyncからSystem.Linq.Asyncが削除されれば、AsyncEnumerableを使った実装に戻す
        return await (await clients.Library.GetLibrary(start, count))
            .ToObservable()
            .SelectMany(async x => await GetPackage(x.Package.Name))
            .ToArray();
    }

    public async Task<Release> Acquire(Package package)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackage", ActivityKind.Client);

        AcquirePackageResponse response = await clients.Library.AcquirePackage(new AcquirePackageRequest
        {
            PackageId = package.Id
        });
        if (response.LatestRelease == null)
            throw new Exception("No release");

        return new Release(package, response.LatestRelease, clients);
    }

    public async Task RemovePackage(Package package)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.RemovePackage", ActivityKind.Client);

        await clients.Library.DeleteLibraryPackage(package.Name);
    }
}
