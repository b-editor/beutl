using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Profile
{
    private readonly BeutlClients _clients;
    private readonly ReactivePropertySlim<ProfileResponse> _response;

    public Profile(ProfileResponse response, BeutlClients clients)
    {
        _clients = clients;
        _response = new(response);

        Id = response.Id;
        Name = Response.Select(x => x.Name).ToReadOnlyReactivePropertySlim()!;
        Biography = Response.Select(x => x.Bio).ToReadOnlyReactivePropertySlim()!;
        DisplayName = Response.Select(x => x.Display_name).ToReadOnlyReactivePropertySlim()!;
        Email = Response.Select(x => x.Email).ToReadOnlyReactivePropertySlim()!;
        TwitterUserName = Response.Select(x => x.Twitter_username).ToReadOnlyReactivePropertySlim()!;
        GitHubUserName = Response.Select(x => x.Github_username).ToReadOnlyReactivePropertySlim()!;
        YouTubeUrl = Response.Select(x => x.Youtube_url).ToReadOnlyReactivePropertySlim()!;
        BlogUrl = Response.Select(x => x.Blog_url).ToReadOnlyReactivePropertySlim()!;
        Followers = Response.Select(x => x.Followers).ToReadOnlyReactivePropertySlim()!;
        Following = Response.Select(x => x.Following).ToReadOnlyReactivePropertySlim()!;
        PublicPackages = Response.Select(x => x.Public_packages).ToReadOnlyReactivePropertySlim()!;
    }

    public IReadOnlyReactiveProperty<ProfileResponse> Response => _response;

    public string Id { get; }

    public IReadOnlyReactiveProperty<string> Name { get; }

    public IReadOnlyReactiveProperty<string> Biography { get; }

    public IReadOnlyReactiveProperty<string> DisplayName { get; }

    public IReadOnlyReactiveProperty<string> Email { get; }

    public IReadOnlyReactiveProperty<string> TwitterUserName { get; }

    public IReadOnlyReactiveProperty<string> GitHubUserName { get; }

    public IReadOnlyReactiveProperty<string> YouTubeUrl { get; }

    public IReadOnlyReactiveProperty<string> BlogUrl { get; }

    public IReadOnlyReactiveProperty<int> Followers { get; }

    public IReadOnlyReactiveProperty<int> Following { get; }

    public IReadOnlyReactiveProperty<int> PublicPackages { get; }

    public async Task RefreshAsync()
    {
        _response.Value = await _clients.Users.GetUserAsync(Name.Value);
    }

    public async Task UpdateAsync(UpdateProfileRequest request)
    {
        _response.Value = await _clients.Users.PatchAsync(Name.Value, request);
    }

    public async Task<Package> AddPackageAsync(string name, CreatePackageRequest request)
    {
        PackageResponse response = await _clients.Packages.PostAsync(Name.Value, name, request);
        return new Package(this, response, _clients);
    }

    public async Task<Package[]> GetPackagesAsync(int start = 0, int count = 30)
    {
        return await (await _clients.Users.GetPackagesAsync(Name.Value, start, count))
            .ToAsyncEnumerable()
            .SelectAwait(async x => await _clients.Packages.GetPackage2Async(x.Id))
            .Select(x => new Package(this, x, _clients))
            .ToArrayAsync();
    }
}
