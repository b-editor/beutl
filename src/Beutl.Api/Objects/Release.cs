using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Release
{
    private readonly BeutlClients _clients;
    private readonly ReactivePropertySlim<ReleaseResponse> _response;
    private readonly ReactivePropertySlim<ReleaseResource[]?> _resources = new();

    public Release(Package package, ReleaseResponse response, BeutlClients clients)
    {
        Package = package;
        _clients = clients;
        _response = new ReactivePropertySlim<ReleaseResponse>(response);

        Version = _response.Select(x => new Version(x.Version)).ToReadOnlyReactivePropertySlim()!;
        Title = _response.Select(x => x.Title).ToReadOnlyReactivePropertySlim()!;
        Body = _response.Select(x => x.Body).ToReadOnlyReactivePropertySlim()!;
        IsPublic = _response.Select(x => x.Public).ToReadOnlyReactivePropertySlim()!;
    }

    public Package Package { get; }

    public long Id { get; }

    public IReadOnlyReactiveProperty<ReleaseResponse> Response => _response;

    public IReadOnlyReactiveProperty<Version> Version { get; }

    public IReadOnlyReactiveProperty<string> Title { get; }

    public IReadOnlyReactiveProperty<string> Body { get; }

    public IReadOnlyReactiveProperty<bool> IsPublic { get; }

    public IReadOnlyReactiveProperty<ReleaseResource[]?> Resources => _resources;

    public async Task RefreshAsync()
    {
        _response.Value = await _clients.Releases.GetReleaseAsync(
            Package.Owner.Value.Name, Package.Name.Value, _response.Value.Version);

        _resources.Value = await GetResourcesAsync();
    }

    private async Task<ReleaseResource[]> GetResourcesAsync()
    {
        return (await _clients.ReleaseResources.GetResourcesAsync(Package.Owner.Value.Name, Package.Name.Value, Response.Value.Version))
            .Select(x => new ReleaseResource(this, x, _clients))
            .ToArray();
    }
}

