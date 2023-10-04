using System.Diagnostics;
using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class Asset
{
    private readonly BeutlApiApplication _clients;
    private readonly ReactivePropertySlim<AssetMetadataResponse> _response;
    private readonly ReactivePropertySlim<bool> _isDeleted = new();

    public Asset(Profile profile, AssetMetadataResponse response, BeutlApiApplication clients)
    {
        _clients = clients;
        _response = new ReactivePropertySlim<AssetMetadataResponse>(response);

        Owner = profile;
        Id = response.Id;
        Name = response.Name;
        AssetType = response.Asset_type;
        Size = response.Size;
        ContentType = response.Content_type;
        DownloadUrl = response.Download_url;
        Sha256 = response.Sha256;
        Sha384 = response.Sha384;
        Sha512 = response.Sha512;
        IsPublic = _response.Select(x => x.Public).ToReadOnlyReactivePropertySlim()!;
    }

    public IReadOnlyReactiveProperty<AssetMetadataResponse> Response => _response;

    public Profile Owner { get; }

    public long Id { get; }

    public string Name { get; }

    public AssetType AssetType { get; }

    public long? Size { get; }

    public string ContentType { get; }

    public string DownloadUrl { get; }

    public string? Sha256 { get; }

    public string? Sha384 { get; }

    public string? Sha512 { get; }

    public IReadOnlyReactiveProperty<bool> IsPublic { get; }

    public MyAsyncLock Lock => _clients.Lock;

    public async Task RefreshAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Asset.Refresh", ActivityKind.Client);

        _response.Value = await _clients.Assets.GetAssetAsync(Owner.Name, Name);
        _isDeleted.Value = false;
    }

    public async Task UpdateAsync(UpdateAssetRequest request)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Asset.Update", ActivityKind.Client);

        if (_isDeleted.Value)
        {
            throw new InvalidOperationException("This object has been deleted.");
        }

        _response.Value = await _clients.Assets.PatchAsync(Owner.Name, Name, request);
    }

    public async Task UpdateAsync(bool isPublic)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Asset.Update", ActivityKind.Client);

        await UpdateAsync(new UpdateAssetRequest(isPublic));
    }

    public async Task DeleteAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Asset.Delete", ActivityKind.Client);

        await _clients.Assets.DeleteAsync(Owner.Name, Name);

        _isDeleted.Value = true;
    }
}
