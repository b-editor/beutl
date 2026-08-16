using System.Diagnostics;
using Beutl.Api.Clients;
using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public class LibraryService(BeutlApiApplication clients) : IBeutlApiResource
{
    public async Task<Package> GetPackage(string name, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackage", ActivityKind.Client);
        PackageResponse package = await clients.Packages.GetPackage(name, token);
        token.ThrowIfCancellationRequested();
        var owner = new Profile(package.Owner, clients);

        return new Package(owner, package, clients);
    }

    public async Task<Profile> GetProfile(string name, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetProfile", ActivityKind.Client);
        ProfileResponse response = await clients.Users.GetUser(name, token);
        token.ThrowIfCancellationRequested();
        return new Profile(response, clients);
    }

    public async Task<Package[]> GetPackages(
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackages", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        AcquirePackageResponse[] packages = await clients.Library.GetLibrary(token, start, count);
        Package[] result = await Task.WhenAll(
            packages.Select(package => GetPackage(package.Package.Name, token)));
        token.ThrowIfCancellationRequested();
        return result;
    }

    public async Task<Release> Acquire(Package package, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.GetPackage", ActivityKind.Client);

        AcquirePackageResponse response = await clients.Library.AcquirePackage(new AcquirePackageRequest
        {
            PackageId = package.Id
        }, token);
        token.ThrowIfCancellationRequested();
        if (response.LatestRelease == null)
            throw new Exception("No release");

        return new Release(package, response.LatestRelease, clients);
    }

    public async Task RemovePackage(Package package, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = clients.ActivitySource.StartActivity("LibraryService.RemovePackage", ActivityKind.Client);

        await clients.Library.DeleteLibraryPackage(package.Name, token);
    }
}
