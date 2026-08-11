using System.Diagnostics;
using System.Reactive.Linq;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Profile
{
    private readonly BeutlApiApplication _clients;
    private readonly ReactivePropertySlim<ProfileResponse> _response;
    private readonly ReadOnlyReactivePropertySlim<ProfileResponse> _readOnlyResponse;
    private readonly object _stateGate = new();
    private string _name;
    private long _refreshVersion;

    public Profile(ProfileResponse response, BeutlApiApplication clients)
    {
        _clients = clients;
        _response = new ReactivePropertySlim<ProfileResponse>(response);
        _readOnlyResponse = _response.ToReadOnlyReactivePropertySlim(response);

        Id = response.Id;
        _name = response.Name;
        Biography = Response.Select(x => x.Bio).ToReadOnlyReactivePropertySlim()!;
        DisplayName = Response.Select(x => x.DisplayName).ToReadOnlyReactivePropertySlim()!;
        AvatarUrl = Response.Select(x => x.IconUrl).ToReadOnlyReactivePropertySlim();
    }

    public IReadOnlyReactiveProperty<ProfileResponse> Response => _readOnlyResponse;

    public string Id { get; }

    public string Name
    {
        get
        {
            lock (_stateGate)
                return _name;
        }
    }

    public IReadOnlyReactiveProperty<string> Biography { get; }

    public IReadOnlyReactiveProperty<string> DisplayName { get; }

    public IReadOnlyReactiveProperty<string?> AvatarUrl { get; }

    public MyAsyncLock Lock => _clients.Lock;

    public async Task RefreshAsync(CancellationToken cancellationToken, bool self = false)
    {
        using CancellationTokenSource lifetimeCts = _clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        long refreshVersion = Interlocked.Increment(ref _refreshVersion);
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.Refresh", ActivityKind.Client);

        ProfileResponse response;
        AuthenticatedUser? authenticatedUser = null;
        if (self)
        {
            authenticatedUser = _clients.AuthenticatedUser.Value
                ?? throw new AuthenticationRequiredException();
            if (!ReferenceEquals(authenticatedUser.Profile, this))
                throw new AuthenticationRequiredException();
            AuthenticatedApiResult<ProfileResponse> result = await _clients.SendAuthenticatedAsync(
                (authorization, requestToken) => _clients.Users.GetSelf(authorization, requestToken),
                token,
                authenticatedUser);
            response = result.Value;
        }
        else
        {
            string name = Name;
            response = await _clients.Users.GetUser(name, token);
        }

        token.ThrowIfCancellationRequested();
        void CommitResponse()
        {
            lock (_stateGate)
            {
                if (refreshVersion != Volatile.Read(ref _refreshVersion))
                    return;

                _response.Value = response;
                _name = response.Name;
            }
        }

        if (authenticatedUser is null)
        {
            CommitResponse();
        }
        else
        {
            _clients.CommitForAuthenticatedUser(authenticatedUser, CommitResponse, token);
        }
    }

    public async Task<Package[]> GetPackagesAsync(
        CancellationToken cancellationToken,
        int start = 0,
        int count = 30)
    {
        using CancellationTokenSource lifetimeCts = _clients.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = _clients.ActivitySource.StartActivity("Profile.GetPackages", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        string name = Name;
        SimplePackageResponse[] packages = await _clients.Users.GetUserPackages(
            name,
            token,
            start,
            count);
        PackageResponse[] responses = await Task.WhenAll(
            packages.Select(package => _clients.Packages.GetPackage(package.Name, token)));
        token.ThrowIfCancellationRequested();
        return responses.Select(response => new Package(this, response, _clients)).ToArray();
    }
}
