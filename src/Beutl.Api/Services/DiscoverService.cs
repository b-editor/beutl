using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Beutl.Api.Clients;
using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public class DiscoverService(BeutlApiApplication clients) : IBeutlApiResource
{
    public MyAsyncLock Lock => clients.Lock;

    public async Task<Package> GetPackage(string name, CancellationToken cancellationToken)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetPackage", ActivityKind.Client);

        PackageResponse package = await clients.Packages.GetPackage(name, cancellationToken).ConfigureAwait(false);
        var owner = new Profile(package.Owner, clients);

        return new Package(owner, package, clients);
    }

    public async Task<Profile> GetProfile(string name, CancellationToken cancellationToken)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetProfile", ActivityKind.Client);

        ProfileResponse response = await clients.Users.GetUser(name, cancellationToken).ConfigureAwait(false);
        return new Profile(response, clients);
    }

    public async Task<Package[]> GetFeatured(
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30,
        PackageKindFilter type = PackageKindFilter.All)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetFeatured", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);
        activity?.SetTag("type", type.ToString());

        // TODO: System.Interactive.AsyncからSystem.Linq.Asyncが削除されれば、AsyncEnumerableを使った実装に戻す
        return await (await clients.Discover.GetFeatured(cancellationToken, start, count, type.ToQueryValue()).ConfigureAwait(false))
            .ToObservable()
            .SelectMany(async x => await GetPackage(x.Name, cancellationToken).ConfigureAwait(false))
            .ToArray()
            .ToTask()
            .ConfigureAwait(false);
    }

    public async Task<Package[]> Search(
        string query,
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30,
        PackageKindFilter type = PackageKindFilter.All)
    {
        // TODO: System.Interactive.AsyncからSystem.Linq.Asyncが削除されれば、AsyncEnumerableを使った実装に戻す
        return await (await clients.Discover.Search(query, cancellationToken, start, count, type.ToQueryValue()).ConfigureAwait(false))
            .ToObservable()
            .SelectMany(async x => await GetPackage(x.Name, cancellationToken).ConfigureAwait(false))
            .ToArray()
            .ToTask()
            .ConfigureAwait(false);
    }
}
