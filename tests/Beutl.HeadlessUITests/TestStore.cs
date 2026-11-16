using System.Net;
using System.Text;
using System.Text.Json;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;

namespace Beutl.HeadlessUITests;

// Store API responses and objects for the package tests.
internal static class TestStore
{
    public static ProfileResponse CreateOwner() => new()
    {
        Id = "owner",
        Name = "owner",
        DisplayName = "Owner",
        Bio = null,
        IconId = null,
        IconUrl = null,
    };

    public static PackageResponse CreatePackageResponse(string name, string description = "") => new()
    {
        Id = "package",
        Owner = CreateOwner(),
        Name = name,
        DisplayName = "Package",
        Description = description,
        ShortDescription = "",
        WebSite = "",
        Tags = [],
        LogoId = null,
        LogoUrl = null,
        Screenshots = [],
        Currency = null,
        Price = null,
        Paid = false,
        Owned = true,
    };

    public static ReleaseResponse CreateReleaseResponse(string version, string description = "", string? fileId = null, string? targetVersion = null) => new()
    {
        Id = version,
        Version = version,
        Title = version,
        Description = description,
        TargetVersion = targetVersion,
        FileId = fileId,
        FileUrl = null,
    };

    // A release of the package named name.
    public static Release CreateRelease(BeutlApiApplication app, string version, string? fileId = null, string name = "Package")
    {
        PackageResponse package = CreatePackageResponse(name);
        return new Release(new Package(new Profile(package.Owner, app), package, app), CreateReleaseResponse(version, fileId: fileId), app);
    }
}

// Serves the package and its releases and records the requests.
internal sealed class StoreHandler(PackageResponse package, params ReleaseResponse[] releases) : HttpMessageHandler
{
    public PackageResponse Package { get; set; } = package;

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Uri uri = request.RequestUri!;
        object response = Package;
        if (request.Method == HttpMethod.Post && uri.AbsolutePath == "/api/v3/account/library")
        {
            response = new { Package, LatestRelease = releases.FirstOrDefault() };
        }
        else if (uri.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            int start = int.Parse(query["start"] ?? "0");
            int count = int.Parse(query["count"] ?? "30");
            response = releases.Skip(start).Take(count).ToArray();
        }
        else if (uri.Segments is [.., "releases/", var version] && releases.FirstOrDefault(r => r.Version == version) is { } release)
        {
            response = release;
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json"),
        });
    }
}

// A BeutlApiApplication to build store objects with, for tests that send no requests.
internal sealed class StoreClients : IAsyncDisposable
{
    private readonly HttpClient _httpClient = new();

    public StoreClients() => App = new BeutlApiApplication(_httpClient, new ExtensionProvider());

    public BeutlApiApplication App { get; }

    public Release CreateRelease(string version) => TestStore.CreateRelease(App, version);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await App.DisposeAsync();
        }
        finally
        {
            _httpClient.Dispose();
        }
    }
}
