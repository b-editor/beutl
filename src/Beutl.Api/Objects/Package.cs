using System.Diagnostics;
using System.Reactive.Linq;

using Nito.AsyncEx;

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

    public string Name { get; }

    public IReadOnlyReactiveProperty<string?> DisplayName { get; }

    public IReadOnlyReactiveProperty<string?> Description { get; }

    public IReadOnlyReactiveProperty<string?> ShortDescription { get; }

    public IReadOnlyReactiveProperty<string?> WebSite { get; }

    public IReadOnlyReactiveProperty<ICollection<string>> Tags { get; }

    public IReadOnlyReactiveProperty<long?> LogoId { get; }

    public IReadOnlyReactiveProperty<string?> LogoUrl { get; }

    public IReadOnlyReactiveProperty<IDictionary<string, string>?> Screenshots { get; }

    public IReadOnlyReactiveProperty<bool> IsPublic { get; }

    public IReadOnlyReactiveProperty<bool> IsDeleted => _isDeleted;

    public MyAsyncLock Lock => _clients.Lock;

    public async Task RefreshAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.Refresh", ActivityKind.Client);

        _response.Value = await _clients.Packages.GetPackageAsync(Name);
        _isDeleted.Value = false;
    }

    public async Task UpdateAsync(UpdatePackageRequest request)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.Update", ActivityKind.Client);

        if (_isDeleted.Value)
        {
            throw new InvalidOperationException("This object has been deleted.");
        }

        _response.Value = await _clients.Packages.PatchAsync(Name, request);
    }

    public async Task UpdateAsync(
        string? description = null,
        string? displayName = null,
        long? logoImageId = null,
        bool? isPublic = null,
        ICollection<long>? screenshots = null,
        string? shortDescription = null,
        ICollection<string>? tags = null,
        string? website = null)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.Update", ActivityKind.Client);

        if (_isDeleted.Value)
        {
            throw new InvalidOperationException("This object has been deleted.");
        }

        _response.Value = await _clients.Packages.PatchAsync(Name, new UpdatePackageRequest(
            description: description,
            display_name: displayName,
            logo_image_id: logoImageId,
            @public: isPublic,
            screenshots: screenshots,
            short_description: shortDescription,
            tags: tags,
            website: website));
    }

    public async Task DeleteAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.Delete", ActivityKind.Client);

        FileResponse response = await _clients.Packages.DeleteAsync(Name);

        response.Dispose();

        _isDeleted.Value = true;
    }

    public async Task<Release> GetReleaseAsync(string version)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.GetRelease", ActivityKind.Client);
        ReleaseResponse response = await _clients.Releases.GetReleaseAsync(Name, version);
        return new Release(this, response, _clients);
    }

    public async Task<Release[]> GetReleasesAsync(int start = 0, int count = 30)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.GetReleases", ActivityKind.Client);
        activity?.SetTag("start", start);
        activity?.SetTag("count", count);

        return (await _clients.Releases.GetReleasesAsync(Name, start, count))
            .Select(x => new Release(this, x, _clients))
            .ToArray();
    }

    public async Task<Release> AddReleaseAsync(string version, CreateReleaseRequest request)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Package.AddRelease", ActivityKind.Client);

        ReleaseResponse response = await _clients.Releases.PostAsync(Name, version, request);
        return new Release(this, response, _clients);
    }
}
