using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Beutl.Api.Objects;

public class DiscoverService
{
    private readonly BeutlClients _clients;

    public DiscoverService(BeutlClients clients)
    {
        _clients = clients;
    }

    public async Task<Package> GetPackage(long id)
    {
        PackageResponse package = await _clients.Packages.GetPackage2Async(id);
        Profile owner = await GetProfileById(package.Owner.Id);

        return new Package(owner, package, _clients);
    }

    public async Task<Profile> GetProfileById(string id)
    {
        ProfileResponse response = await _clients.Users.GetUserAsync(id);
        return new Profile(response, _clients);
    }

    public async Task<Package[]> GetDailyRanking(int start = 0, int count = 30)
    {
        return await (await _clients.Discover.GetDailyAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Id))
            .ToArrayAsync();
    }

    public async Task<Package[]> GetWeeklyRanking(int start = 0, int count = 30)
    {
        return await (await _clients.Discover.GetWeeklyAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Id))
            .ToArrayAsync();
    }

    public async Task<Package[]> GetOverallRanking(int start = 0, int count = 30)
    {
        return await (await _clients.Discover.GetOverallAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Id))
            .ToArrayAsync();
    }

    public async Task<Package[]> GetRecentlyRanking(int start = 0, int count = 30)
    {
        return await (await _clients.Discover.GetRecentlyAsync(start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Id))
            .ToArrayAsync();
    }

    public async Task<Package[]> SearchPackages(string query, int start = 0, int count = 30)
    {
        return await (await _clients.Discover.SearchPackagesAsync(query, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Id))
            .ToArrayAsync();
    }
    
    public async Task<Profile[]> SearchUsers(string query, int start = 0, int count = 30)
    {
        return await (await _clients.Discover.SearchUsersAsync(query, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetProfileById(x.Id))
            .ToArrayAsync();
    }

    public async Task<Package[]> Search(string query, int start = 0, int count = 30)
    {
        return await (await _clients.Discover.SearchAsync(query, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await GetPackage(x.Id))
            .ToArrayAsync();
    }
}
