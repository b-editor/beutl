using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Package
{
    private readonly BeutlClients _clients;
    private readonly ReactivePropertySlim<PackageResponse> _response;
    private readonly ReactivePropertySlim<bool> _isDeleted = new();

    public Package(Profile profile, PackageResponse response, BeutlClients clients)
    {
        _clients = clients;
        _response = new ReactivePropertySlim<PackageResponse>(response);
        Owner = profile;

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

    public Profile Owner { get; }

    public long Id { get; }

    public IReadOnlyReactiveProperty<string> Name { get; }

    public IReadOnlyReactiveProperty<string> DisplayName { get; }

    public IReadOnlyReactiveProperty<string> Description { get; }

    public IReadOnlyReactiveProperty<string> ShortDescription { get; }

    public IReadOnlyReactiveProperty<string> WebSite { get; }

    public IReadOnlyReactiveProperty<ICollection<string>> Tags { get; }

    public IReadOnlyReactiveProperty<bool> IsPublic { get; }

    public IReadOnlyReactiveProperty<bool> IsDeleted => _isDeleted;

    public async Task RefreshAsync()
    {
        _response.Value = await _clients.Packages.GetPackage2Async(Id);
        _isDeleted.Value = false;
    }

    public async Task UpdateAsync(UpdatePackageRequest request)
    {
        if (_isDeleted.Value)
        {
            throw new InvalidOperationException("This object has been deleted.");
        }

        _response.Value = await _clients.Packages.Patch2Async(Id, request);
    }

    public async Task DeleteAsync()
    {
        FileResponse response = await _clients.Packages.Delete2Async(Id);

        response.Dispose();

        _isDeleted.Value = true;
    }

    public async Task<Release[]> GetReleasesAsync(int start = 0, int count = 30)
    {
        return (await _clients.Releases.GetReleasesAsync(Owner.Name.Value, Name.Value, start, count))
            .Select(x => new Release(this, x, _clients))
            .ToArray();
    }

    public async Task<PackageResource> AddResourceAsync(string locale, CreatePackageResourceRequest request)
    {
        PackageResourceResponse response = await _clients.PackageResources.PostAsync(Owner.Name.Value, Name.Value, locale, request);

        return new PackageResource(this, response, _clients);
    }

    public async Task<Release> AddReleaseAsync(string version, CreateReleaseRequest request)
    {
        ReleaseResponse response = await _clients.Releases.PostAsync(Owner.Name.Value, Name.Value, version, request);
        return new Release(this, response, _clients);
    }

    public async Task<PackageResource[]> GetResourcesAsync()
    {
        return (await _clients.PackageResources.GetResourcesAsync(Owner.Name.Value, _response.Value.Name))
            .Select(x => new PackageResource(this, x, _clients))
            .ToArray();
    }
}
