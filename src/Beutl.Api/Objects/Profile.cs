using System.Diagnostics;
using System.Reactive.Linq;
using Beutl.Api.Clients;
using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Profile
{
    private readonly BeutlApiApplication _clients;
    private readonly ReactivePropertySlim<ProfileResponse> _response;

    public Profile(ProfileResponse response, BeutlApiApplication clients)
    {
        _clients = clients;
        _response = new ReactivePropertySlim<ProfileResponse>(response);

        Id = response.Id;
        Name = response.Name;
        Biography = Response.Select(x => x.Bio).ToReadOnlyReactivePropertySlim()!;
        DisplayName = Response.Select(x => x.DisplayName).ToReadOnlyReactivePropertySlim()!;
        AvatarUrl = Response.Select(x => x.IconUrl).ToReadOnlyReactivePropertySlim();
    }

    public IReadOnlyReactiveProperty<ProfileResponse> Response => _response;

    public string Id { get; }

    public string Name { get; private set; }

    public IReadOnlyReactiveProperty<string> Biography { get; }

    public IReadOnlyReactiveProperty<string> DisplayName { get; }

    public IReadOnlyReactiveProperty<string?> AvatarUrl { get; }

    public MyAsyncLock Lock => _clients.Lock;

    public async Task RefreshAsync(bool self = false)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.Refresh", ActivityKind.Client);

        if (self)
        {
            _response.Value = await _clients.Users.GetSelf();
            Name = _response.Value.Name;
        }
        else
        {
            _response.Value = await _clients.Users.GetUser(Name);
        }
    }

    public async Task<Package[]> GetPackagesAsync(int start = 0, int count = 30)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.GetPackages", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        // TODO: System.Interactive.AsyncからSystem.Linq.Asyncが削除されれば、AsyncEnumerableを使った実装に戻す
        return await (await _clients.Users.GetUserPackages(Name, start, count))
            .ToObservable()
            .SelectMany(async x => await _clients.Packages.GetPackage(x.Name))
            .Select(x => new Package(this, x, _clients))
            .ToArray();
    }
}
