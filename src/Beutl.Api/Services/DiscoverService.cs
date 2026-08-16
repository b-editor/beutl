using System.Diagnostics;
using Beutl.Api.Clients;
using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public class DiscoverService(BeutlApiApplication clients) : IBeutlApiResource
{
    public MyAsyncLock Lock => clients.Lock;

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

    public async Task<Profile> GetProfile(string name, CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetProfile", ActivityKind.Client);

        ProfileResponse response = await clients.Users.GetUser(name, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new Profile(response, clients);
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
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetDailyRanking", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);
        activity?.SetTag("type", type.ToString());

        SimplePackageResponse[] packages = await clients.Discover
            .GetFeatured(token, start, count, type.ToQueryValue())
            .ConfigureAwait(false);
        Package[] result = await Task.WhenAll(
                packages.Select(package => GetPackage(package.Name, token)))
            .ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
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
        Package[] result = await Task.WhenAll(
                packages.Select(package => GetPackage(package.Name, token)))
            .ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
