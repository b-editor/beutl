using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Beutl.Api.Clients;
using Beutl.Api.Objects;

namespace Beutl.Api.Services;

public class DiscoverService(BeutlApiApplication clients) : IBeutlApiResource
{
    public MyAsyncLock Lock => clients.Lock;

    public async Task<Package> GetPackage(string name)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetPackage", ActivityKind.Client);

        PackageResponse package = await clients.Packages.GetPackage(name).ConfigureAwait(false);
        var owner = new Profile(package.Owner, clients);

        return new Package(owner, package, clients);
    }

    public async Task<Profile> GetProfile(string name)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetProfile", ActivityKind.Client);

        ProfileResponse response = await clients.Users.GetUser(name).ConfigureAwait(false);
        return new Profile(response, clients);
    }

    public async Task<Package[]> GetFeatured(int start = 0, int count = 30)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("DiscoverService.GetDailyRanking", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        // TODO: System.Interactive.AsyncからSystem.Linq.Asyncが削除されれば、AsyncEnumerableを使った実装に戻す
        return await (await clients.Discover.GetFeatured(start, count).ConfigureAwait(false))
            .ToObservable()
            .SelectMany(async x => await GetPackage(x.Name).ConfigureAwait(false))
            .ToArray()
            .ToTask()
            .ConfigureAwait(false);
    }

    public async Task<Package[]> Search(string query, int start = 0, int count = 30)
    {
        // TODO: System.Interactive.AsyncからSystem.Linq.Asyncが削除されれば、AsyncEnumerableを使った実装に戻す
        return await (await clients.Discover.Search(query, start, count).ConfigureAwait(false))
            .ToObservable()
            .SelectMany(async x => await GetPackage(x.Name).ConfigureAwait(false))
            .ToArray()
            .ToTask()
            .ConfigureAwait(false);
    }
}
