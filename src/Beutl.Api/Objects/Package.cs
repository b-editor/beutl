using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Package
{
    private readonly BeutlClients _clients;
    private readonly ReactivePropertySlim<PackageResponse> _response;
    private readonly ReactivePropertySlim<ProfileResponse> _owner;
    private readonly ReactivePropertySlim<PackageResource[]?> _resources = new();

    public Package(PackageResponse response, ProfileResponse profile, BeutlClients clients)
    {
        _clients = clients;
        _response = new ReactivePropertySlim<PackageResponse>(response);
        _owner = new ReactivePropertySlim<ProfileResponse>(profile);

        Id = response.Id;
        Name = _response.Select(x => x.Name).ToReadOnlyReactivePropertySlim()!;
        DisplayName = _response.Select(x => x.Display_name).ToReadOnlyReactivePropertySlim()!;
        Description = _response.Select(x => x.Description).ToReadOnlyReactivePropertySlim()!;
        ShortDescription = _response.Select(x => x.Short_description).ToReadOnlyReactivePropertySlim()!;
        WebSite = _response.Select(x => x.Website).ToReadOnlyReactivePropertySlim()!;
        Tags = _response.Select(x => x.Tags).ToReadOnlyReactivePropertySlim()!;
        IsPublic = _response.Select(x => x.Public).ToReadOnlyReactivePropertySlim()!;
    }

    public IReadOnlyReactiveProperty<PackageResponse> Response => _response;

    public IReadOnlyReactiveProperty<ProfileResponse> Owner => _owner;

    public long Id { get; }

    public IReadOnlyReactiveProperty<string> Name { get; }

    public IReadOnlyReactiveProperty<string> DisplayName { get; }

    public IReadOnlyReactiveProperty<string> Description { get; }

    public IReadOnlyReactiveProperty<string> ShortDescription { get; }

    public IReadOnlyReactiveProperty<string> WebSite { get; }

    public IReadOnlyReactiveProperty<ICollection<string>> Tags { get; }

    public IReadOnlyReactiveProperty<bool> IsPublic { get; }

    public IReadOnlyReactiveProperty<PackageResource[]?> Resources => _resources;

    public async Task RefreshAsync()
    {
        PackageResponse response = await _clients.Packages.GetPackage2Async(Id);
        if (response.Owner.Id != _owner.Value.Id
            || response.Owner.Name != _owner.Value.Name)
        {
            _owner.Value = await _clients.Users.GetUserAsync(response.Owner.Name);
        }

        _response.Value = response;

        _resources.Value = await GetResourcesAsync();
    }

    public async Task UpdateAsync(UpdatePackageRequest request)
    {
        _response.Value = await _clients.Packages.Patch2Async(Id, request);
    }

    public async Task<Release[]> GetReleaseAsync(int start = 0, int count = 30)
    {
        return (await _clients.Releases.GetReleasesAsync(Owner.Value.Name, Name.Value, start, count))
            .Select(x => new Release(this, x, _clients))
            .ToArray();
    }

    public async Task<Release> AddReleaseAsync(string version, CreateReleaseRequest request)
    {
        ReleaseResponse response = await _clients.Releases.PostAsync(Owner.Value.Name, Name.Value, version, request);
        return new Release(this, response, _clients);
    }

    private async Task<PackageResource[]> GetResourcesAsync()
    {
        return (await _clients.PackageResources.GetResourcesAsync(_owner.Value.Name, _response.Value.Name))
            .Select(x => new PackageResource(this, x, _clients))
            .ToArray();
    }
}
