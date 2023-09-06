using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Beutl.Api.Objects;

using Nito.AsyncEx;

namespace Beutl.Api.Services;

public class DiscoverService : IBeutlApiResource
{
    private readonly BeutlApiApplication _clients;

    public DiscoverService(BeutlApiApplication clients)
    {
        _clients = clients;
    }

    public MyAsyncLock Lock => _clients.Lock;

    public async Task<Package> GetPackage(string name)
    {
        PackageResponse package = await _clients.Packages.GetPackageAsync(name);
        Profile owner = await GetProfile(package.Owner.Name);

        return new Package(owner, package, _clients);
    }

    public async Task<Profile> GetProfile(string name)
    {
        ProfileResponse response = await _clients.Users.GetUserAsync(name);
        return new Profile(response, _clients);
    }

    public async Task<Package[]> GetDailyRanking(int start = 0, int count = 30)
    {
        return await (await _clients.Discover.GetDailyAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Name))
            .ToArrayAsync();
    }

    public async Task<Package[]> GetWeeklyRanking(int start = 0, int count = 30)
    {
        return await (await _clients.Discover.GetWeeklyAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Name))
            .ToArrayAsync();
    }

    public async Task<Package[]> GetOverallRanking(int start = 0, int count = 30)
    {
        return await (await _clients.Discover.GetOverallAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Name))
            .ToArrayAsync();
    }

    public async Task<Package[]> GetRecentlyRanking(int start = 0, int count = 30)
    {
        return await (await _clients.Discover.GetRecentlyAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Name))
            .ToArrayAsync();
    }

    public async Task<Package[]> SearchPackages(string query, int start = 0, int count = 30)
    {
        return await (await _clients.Discover.SearchPackagesAsync(query, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Name))
            .ToArrayAsync();
    }

    public async Task<Profile[]> SearchUsers(string query, int start = 0, int count = 30)
    {
        return await (await _clients.Discover.SearchUsersAsync(query, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetProfile(x.Name))
            .ToArrayAsync();
    }

    public async Task<Package[]> Search(string query, int start = 0, int count = 30)
    {
        return await (await _clients.Discover.SearchAsync(query, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Name))
            .ToArrayAsync();
    }
}
