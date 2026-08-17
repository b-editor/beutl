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

    public async Task RefreshAsync(CancellationToken cancellationToken, bool self = false)
    {
        using CancellationTokenSource lifetimeCts = _clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.Refresh", ActivityKind.Client);

        if (self)
        {
            ProfileResponse response = await _clients.Users.GetSelf(token);
            token.ThrowIfCancellationRequested();
            _response.Value = response;
            Name = _response.Value.Name;
        }
        else
        {
            ProfileResponse response = await _clients.Users.GetUser(Name, token);
            token.ThrowIfCancellationRequested();
            _response.Value = response;
        }
    }

    public async Task<Package[]> GetPackagesAsync(
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30)
    {
        using CancellationTokenSource lifetimeCts = _clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.GetPackages", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        // TODO: System.Interactive.AsyncからSystem.Linq.Asyncが削除されれば、AsyncEnumerableを使った実装に戻す
        SimplePackageResponse[] packages = await _clients.Users.GetUserPackages(Name, token, start, count);
        token.ThrowIfCancellationRequested();
        // Await every spawned request so a fault in one does not sever the others from
        // the lifetime token; SelectMany would terminate the sequence on the first fault
        // and leave the remaining requests running unlinked.
        Package[] result = await Task.WhenAll(
            packages.Select(async x =>
            {
                PackageResponse response = await _clients.Packages.GetPackage(x.Name, token);
                return new Package(this, response, _clients);
            }));
        token.ThrowIfCancellationRequested();
        return result;
    }
}
