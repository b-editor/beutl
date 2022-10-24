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
        LogoId = _response.Select(x => x.Logo_id).ToReadOnlyReactivePropertySlim()!;
        LogoUrl = _response.Select(x => x.Logo_url).ToReadOnlyReactivePropertySlim()!;
        Screenshots = _response.Select(x => x.Screenshots).ToReadOnlyReactivePropertySlim()!;
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

    public IReadOnlyReactiveProperty<long?> LogoId { get; }

    public IReadOnlyReactiveProperty<string> LogoUrl { get; }

    public IReadOnlyReactiveProperty<IDictionary<string, string>> Screenshots { get; }

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

    public async Task UpdateAsync(
        string? description = null,
        string? displayName = null,
        long? logoImageId = null,
        string? name = null,
        bool? isPublic = null,
        ICollection<long>? screenshots = null,
        string? shortDescription = null,
        ICollection<string>? tags = null,
        string? website = null)
    {
        if (_isDeleted.Value)
        {
            throw new InvalidOperationException("This object has been deleted.");
        }

        _response.Value = await _clients.Packages.Patch2Async(Id, new UpdatePackageRequest(
            description: description,
            display_name: displayName,
            logo_image_id: logoImageId,
            name: name,
            @public: isPublic,
            screenshots: screenshots,
            short_description: shortDescription,
            tags: tags,
            website: website));
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

    public async Task<Release> AddReleaseAsync(string version, CreateReleaseRequest request)
    {
        ReleaseResponse response = await _clients.Releases.PostAsync(Owner.Name.Value, Name.Value, version, request);
        return new Release(this, response, _clients);
    }
}
