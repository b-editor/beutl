using System.Globalization;
using System.Reactive.Linq;

using Reactive.Bindings;

namespace Beutl.Api.Objects;

public class PackageResource
{
    private readonly BeutlClients _clients;
    private readonly ReactivePropertySlim<PackageResourceResponse> _response;
    private readonly ReactivePropertySlim<bool> _isDeleted = new();

    public PackageResource(Package package, PackageResourceResponse response, BeutlClients clients)
    {
        Package = package;
        _clients = clients;
        _response = new ReactivePropertySlim<PackageResourceResponse>(response);
        Locale = CultureInfo.GetCultureInfo(response.Locale);

        DisplayName = _response.Select(x => x.Display_name).ToReadOnlyReactivePropertySlim()!;
        Description = _response.Select(x => x.Description).ToReadOnlyReactivePropertySlim()!;
        ShortDescription = _response.Select(x => x.Short_description).ToReadOnlyReactivePropertySlim()!;
        WebSite = _response.Select(x => x.Website).ToReadOnlyReactivePropertySlim()!;
    }

    public Package Package { get; }

    public IReadOnlyReactiveProperty<PackageResourceResponse> Response => _response;

    public CultureInfo Locale { get; }

    public IReadOnlyReactiveProperty<string?> DisplayName { get; }

    public IReadOnlyReactiveProperty<string?> Description { get; }

    public IReadOnlyReactiveProperty<string?> ShortDescription { get; }

    public IReadOnlyReactiveProperty<string?> WebSite { get; }

    public IReadOnlyReactiveProperty<bool> IsDeleted => _isDeleted;

    public async Task RefreshAsync()
    {
        _response.Value = await _clients.PackageResources.GetResourceAsync(
            Package.Owner.Name.Value, Package.Name.Value, _response.Value.Locale);
    }

    public async Task UpdateAsync(UpdatePackageResourceRequest request)
    {
        if (_isDeleted.Value)
        {
            throw new InvalidOperationException("This object has been deleted.");
        }

        _response.Value = await _clients.PackageResources.PatchAsync(
            Package.Owner.Name.Value,
            Package.Name.Value,
            Response.Value.Locale,
            request);
    }

    public async Task DeleteAsync()
    {
        FileResponse response = await _clients.PackageResources.DeleteAsync(
            Package.Owner.Name.Value,
            Package.Name.Value,
            Response.Value.Locale);

        response.Dispose();

        _isDeleted.Value = true;
    }
}
