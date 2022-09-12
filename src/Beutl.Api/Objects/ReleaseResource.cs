using System.Globalization;
using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class ReleaseResource
{
    private readonly BeutlClients _clients;
    private readonly ReactivePropertySlim<ReleaseResourceResponse> _response;

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

    public async Task RefreshAsync()
    {
        _response.Value = await _clients.ReleaseResources.GetResourceAsync(
            owner: Release.Package.Owner.Value.Name,
            name: Release.Package.Name.Value,
            version: Release.Response.Value.Version,
            locale: _response.Value.Locale);
    }
}

