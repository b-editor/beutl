using System.Globalization;
using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class ReleaseResource
{
    private readonly BeutlClients _clients;
    private readonly ReactivePropertySlim<ReleaseResourceResponse> _response;
    private readonly ReactivePropertySlim<bool> _isDeleted = new();

    public ReleaseResource(Release release, ReleaseResourceResponse response, BeutlClients clients)
    {
        Release = release;
        _clients = clients;
        _response = new ReactivePropertySlim<ReleaseResourceResponse>(response);
        Locale = CultureInfo.GetCultureInfo(response.Locale);

        Title = _response.Select(x => x.Title).ToReadOnlyReactivePropertySlim()!;
        Body = _response.Select(x => x.Body).ToReadOnlyReactivePropertySlim()!;
    }

    public Release Release { get; }

    public IReadOnlyReactiveProperty<ReleaseResourceResponse> Response => _response;

    public CultureInfo Locale { get; }

    public IReadOnlyReactiveProperty<string?> Title { get; }

    public IReadOnlyReactiveProperty<string?> Body { get; }

    public IReadOnlyReactiveProperty<bool> IsDeleted => _isDeleted;

    public async Task RefreshAsync()
    {
        _response.Value = await _clients.ReleaseResources.GetResourceAsync(
            owner: Release.Package.Owner.Name.Value,
            name: Release.Package.Name.Value,
            version: Release.Response.Value.Version,
            locale: _response.Value.Locale);

        _isDeleted.Value = false;
    }

    public async Task DeleteAsync()
    {
        FileResponse response = await _clients.ReleaseResources.DeleteAsync(
            Release.Package.Owner.Name.Value,
            Release.Package.Name.Value,
            Release.Response.Value.Version,
            Response.Value.Locale);

        response.Dispose();

        _isDeleted.Value = true;
    }

    public async Task UpdateAsync(UpdateReleaseResourceRequest request)
    {
        if (_isDeleted.Value)
        {
            throw new InvalidOperationException("This object has been deleted.");
        }

        _response.Value = await _clients.ReleaseResources.PatchAsync(
            Release.Package.Owner.Name.Value,
            Release.Package.Name.Value,
            Release.Response.Value.Version,
            Response.Value.Locale,
            request);
    }
}

