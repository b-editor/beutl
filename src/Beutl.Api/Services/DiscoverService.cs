using System.Diagnostics;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Nito.AsyncEx;

namespace Beutl.Api.Services;

public class DiscoverService(BeutlApiApplication clients) : IBeutlApiResource
{
    public AsyncLock Lock => clients.Lock;

    public async Task<Package> GetPackage(string name, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetPackage", ActivityKind.Client);

        PackageResponse package = await clients.Packages.GetPackage(name, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var owner = new Profile(package.Owner, clients);

        return new Package(owner, package, clients);
    }

    public async Task<Package[]> GetFeatured(
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30,
        PackageKindFilter type = PackageKindFilter.All)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetFeatured", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);
        activity?.SetTag("type", type.ToString());

        SimplePackageResponse[] packages = await clients.Discover
            .GetFeatured(token, start, count, type.ToQueryValue())
            .ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return ToPackages(packages);
    }

    public async Task<Package[]> Search(
        string query,
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30,
        PackageKindFilter type = PackageKindFilter.All)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();

        SimplePackageResponse[] packages = await clients.Discover
            .Search(query, token, start, count, type.ToQueryValue())
            .ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return ToPackages(packages);
    }

    // The cards need only the list entries; the details page loads the rest of a package.
    private Package[] ToPackages(SimplePackageResponse[] packages)
    {
        return packages
            .Select(package => new Package(new Profile(package.Owner, clients), package, clients))
            .ToArray();
    }
}
