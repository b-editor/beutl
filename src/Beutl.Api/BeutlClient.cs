

namespace Beutl.Api;

public class BeutlClients
{
    private const string BaseUrl = "https://beutl.beditor.net";

    public BeutlClients(HttpClient httpClient)
    {
        PackageResources = new PackageResourcesClient(httpClient) { BaseUrl = BaseUrl };
        Packages = new PackagesClient(httpClient) { BaseUrl = BaseUrl };
        ReleaseResources = new ReleaseResourcesClient(httpClient) { BaseUrl = BaseUrl };
        Releases = new ReleasesClient(httpClient) { BaseUrl = BaseUrl };
        Users = new UsersClient(httpClient) { BaseUrl = BaseUrl };
        Account = new AccountClient(httpClient) { BaseUrl = BaseUrl };
    }

    public PackageResourcesClient PackageResources { get; }

    public PackagesClient Packages { get; }

    public ReleaseResourcesClient ReleaseResources { get; }

    public ReleasesClient Releases { get; }

    public UsersClient Users { get; }

    public AccountClient Account { get; }
}
