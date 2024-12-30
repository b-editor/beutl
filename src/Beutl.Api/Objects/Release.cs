using System.Diagnostics;
using System.Reactive.Linq;
using Beutl.Api.Clients;
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
        Title = _response.Select(x => x.Title).ToReadOnlyReactivePropertySlim();
        Body = _response.Select(x => x.Description).ToReadOnlyReactivePropertySlim();
        TargetVersion = _response.Select(x => x.TargetVersion).ToReadOnlyReactivePropertySlim();
        AssetId = _response.Select(x => x.FileId).ToReadOnlyReactivePropertySlim();
        AssetUrl = _response.Select(x => x.FileUrl).ToReadOnlyReactivePropertySlim();
    }


    public Package Package { get; }

    public string Id { get; }

    public IReadOnlyReactiveProperty<ReleaseResponse> Response => _response;

    public IReadOnlyReactiveProperty<string> Version { get; }

    public IReadOnlyReactiveProperty<string?> Title { get; }

    public IReadOnlyReactiveProperty<string?> Body { get; }

    public IReadOnlyReactiveProperty<string?> TargetVersion { get; }

    public IReadOnlyReactiveProperty<string?> AssetId { get; }

    public ReadOnlyReactivePropertySlim<string?> AssetUrl { get; set; }

    public async Task RefreshAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Release.Refresh", ActivityKind.Client);

        _response.Value = await _clients.Releases.GetRelease(Package.Name, _response.Value.Version);

        _isDeleted.Value = false;
    }

    public async Task<FileResponse> GetAssetAsync()
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("Release.GetAsset", ActivityKind.Client);

        if (AssetId.Value==null)
            throw new InvalidOperationException("This release has no assets.");

        return await _clients.Files.GetFile(AssetId.Value!);
    }
}

