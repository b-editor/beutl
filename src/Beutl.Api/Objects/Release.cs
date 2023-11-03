using System.Diagnostics;
using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Release
{
    private readonly BeutlApiApplication _clients;
    private readonly ReactivePropertySlim<ReleaseResponse> _response;
    private readonly ReactivePropertySlim<bool> _isDeleted = new();

    public Release(Package package, ReleaseResponse response, BeutlApiApplication clients)
    {
        Id = response.Id;
        Package = package;
        _clients = clients;
        _response = new ReactivePropertySlim<ReleaseResponse>(response);

        Version = _response.Select(x => x.Version).ToReadOnlyReactivePropertySlim()!;
        Title = _response.Select(x => x.Title).ToReadOnlyReactivePropertySlim()!;
        Body = _response.Select(x => x.Body).ToReadOnlyReactivePropertySlim()!;
        TargetVersion = _response.Select(x => x.Target_version).ToReadOnlyReactivePropertySlim()!;
        AssetId = _response.Select(x => x.Asset_id).ToReadOnlyReactivePropertySlim()!;
        IsPublic = _response.Select(x => x.Public).ToReadOnlyReactivePropertySlim()!;
    }

    public Package Package { get; }

    public long Id { get; }

    public IReadOnlyReactiveProperty<ReleaseResponse> Response => _response;

    public IReadOnlyReactiveProperty<string> Version { get; }

    public IReadOnlyReactiveProperty<string?> Title { get; }

    public IReadOnlyReactiveProperty<string?> Body { get; }
    
    public IReadOnlyReactiveProperty<string?> TargetVersion { get; }

    public IReadOnlyReactiveProperty<long?> AssetId { get; }

    public IReadOnlyReactiveProperty<bool> IsPublic { get; }

    public IReadOnlyReactiveProperty<bool> IsDeleted => _isDeleted;

    public async Task RefreshAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Release.Refresh", ActivityKind.Client);

        _response.Value = await _clients.Releases.GetReleaseAsync(Package.Name, _response.Value.Version);

        _isDeleted.Value = false;
    }

    public Task UpdateAsync(long? assetId = null, string? body = null, bool? isPublic = null, string? title = null, string? targetVersion = null)
    {
        return UpdateAsync(new UpdateReleaseRequest(assetId, body, isPublic, targetVersion, title));
    }

    public async Task UpdateAsync(UpdateReleaseRequest request)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Release.Update", ActivityKind.Client);

        if (_isDeleted.Value)
        {
            throw new InvalidOperationException("This object has been deleted.");
        }

        _response.Value = await _clients.Releases.PatchAsync(
            Package.Name,
            Response.Value.Version,
            request);
    }

    public async Task DeleteAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Release.Delete", ActivityKind.Client);

        FileResponse response = await _clients.Releases.DeleteAsync(
            Package.Name,
            Response.Value.Version);

        response.Dispose();

        _isDeleted.Value = true;
    }

    public async Task<Asset> GetAssetAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Release.GetAsset", ActivityKind.Client);

        if (!AssetId.Value.HasValue)
            throw new InvalidOperationException("This release has no assets.");

        AssetMetadataResponse response = await _clients.Assets.GetAsset2Async(AssetId.Value.Value);
        return new Asset(Package.Owner, response, _clients);
    }
}

