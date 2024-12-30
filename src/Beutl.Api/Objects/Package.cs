using System.Diagnostics;
using System.Reactive.Linq;
using Beutl.Api.Clients;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Package
{
    private readonly BeutlApiApplication _clients;
    private readonly ReactivePropertySlim<PackageResponse> _response;
    private readonly ReactivePropertySlim<bool> _isDeleted = new();

    public Package(Profile profile, PackageResponse response, BeutlApiApplication clients)
    {
        _clients = clients;
        _response = new ReactivePropertySlim<PackageResponse>(response);
        Owner = profile;

        Id = response.Id;
        Name = response.Name;
        DisplayName = _response.Select(x => x.DisplayName).ToReadOnlyReactivePropertySlim();
        Description = _response.Select(x => x.Description).ToReadOnlyReactivePropertySlim();
        ShortDescription = _response.Select(x => x.ShortDescription).ToReadOnlyReactivePropertySlim();
        WebSite = _response.Select(x => x.WebSite).ToReadOnlyReactivePropertySlim();
        Tags = _response.Select(x => x.Tags).ToReadOnlyReactivePropertySlim([]);
        LogoId = _response.Select(x => x.LogoId).ToReadOnlyReactivePropertySlim();
        LogoUrl = _response.Select(x => x.LogoUrl).ToReadOnlyReactivePropertySlim();
        Screenshots = _response.Select(x => x.Screenshots).ToReadOnlyReactivePropertySlim([]);
        Currency = _response.Select(x => x.Currency).ToReadOnlyReactivePropertySlim();
        Price = _response.Select(x => x.Price).ToReadOnlyReactivePropertySlim();
        Paid = _response.Select(x => x.Paid).ToReadOnlyReactivePropertySlim();
        Owned = _response.Select(x => x.Owned).ToReadOnlyReactivePropertySlim();
    }

    public IReadOnlyReactiveProperty<PackageResponse> Response => _response;

    public Profile Owner { get; }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyReactiveProperty<string?> DisplayName { get; }

    public IReadOnlyReactiveProperty<string?> Description { get; }

    public IReadOnlyReactiveProperty<string?> ShortDescription { get; }

    public IReadOnlyReactiveProperty<string?> WebSite { get; }

    public IReadOnlyReactiveProperty<string[]> Tags { get; }

    public IReadOnlyReactiveProperty<string?> LogoId { get; }

    public IReadOnlyReactiveProperty<string?> LogoUrl { get; }

    public IReadOnlyReactiveProperty<string[]> Screenshots { get; }

    public IReadOnlyReactiveProperty<string?> Currency { get; }

    public IReadOnlyReactiveProperty<int?> Price { get; }

    public IReadOnlyReactiveProperty<bool> Paid { get; }

    public IReadOnlyReactiveProperty<bool> Owned { get; }

    public MyAsyncLock Lock => _clients.Lock;

    public async Task RefreshAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.Refresh", ActivityKind.Client);

        _response.Value = await _clients.Packages.GetPackage(Name);
        _isDeleted.Value = false;
    }

    public async Task<Release> GetReleaseAsync(string version)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.GetRelease", ActivityKind.Client);
        ReleaseResponse response = await _clients.Releases.GetRelease(Name, version);
        return new Release(this, response, _clients);
    }

    public async Task<Release[]> GetReleasesAsync(int start = 0, int count = 30)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.GetReleases", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        return (await _clients.Releases.GetReleases(Name, start, count))
            .Select(x => new Release(this, x, _clients))
            .ToArray();
    }
}
