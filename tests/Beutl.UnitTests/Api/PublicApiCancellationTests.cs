using System.Net;
using System.Reflection;
using System.Text;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Editor.Services;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class PublicApiCancellationTests
{
    private static readonly string[] s_discoverOperations =
        ["GetPackage", "GetProfile", "GetFeatured", "Search"];

    private static readonly string[] s_libraryOperations =
        ["GetPackage", "GetProfile", "GetPackages", "Acquire", "RemovePackage"];

    private static readonly string[] s_packageOperations =
        ["RefreshAsync", "GetReleaseAsync", "GetReleasesAsync"];

    private static readonly string[] s_releaseOperations =
        ["RefreshAsync", "GetAssetAsync"];

    [Test]
    public void HighLevelOperations_RequireCancellationTokens()
    {
        AssertRequiredCancellationTokens(typeof(DiscoverService), s_discoverOperations);
        AssertRequiredCancellationTokens(typeof(LibraryService), s_libraryOperations);
        AssertRequiredCancellationTokens(typeof(Package), s_packageOperations);
        AssertRequiredCancellationTokens(typeof(Release), s_releaseOperations);
        AssertRequiredCancellationTokens(typeof(IFilesClient), ["GetFile"]);
        AssertRequiredCancellationTokens(typeof(IElementAdder), ["AddAsync"]);
    }

    [Test]
    public void PackageManagerCheckUpdateOperations_RequireCancellationTokens()
    {
        MethodInfo[] methods = typeof(PackageManager)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "CheckUpdate")
            .ToArray();

        Assert.That(methods, Has.Length.EqualTo(2));

        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] cancellationTokens = method.GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(CancellationToken))
                .ToArray();
            Assert.That(
                cancellationTokens,
                Has.Length.EqualTo(1),
                $"PackageManager.{method.Name} must require a CancellationToken.");
            Assert.That(
                cancellationTokens[0].HasDefaultValue,
                Is.False,
                $"PackageManager.{method.Name} must not default its CancellationToken.");
        }
    }

    [Test]
    public void PackageManagerCheckUpdateOperations_ObservePreCanceledTokens()
    {
        using var httpClient = new HttpClient();
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        PackageManager manager = app.GetResource<PackageManager>();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await manager.CheckUpdate(cancellationTokenSource.Token));
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await manager.CheckUpdate("package-name", cancellationTokenSource.Token));
    }

    [TestCaseSource(nameof(s_discoverOperations))]
    public async Task DiscoverOperations_PropagateCancellationToTransport(string operationName)
    {
        using var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        var service = new DiscoverService(app);

        Task operation = operationName switch
        {
            "GetPackage" => service.GetPackage("package-name", cancellationTokenSource.Token),
            "GetProfile" => service.GetProfile("profile-name", cancellationTokenSource.Token),
            "GetFeatured" => service.GetFeatured(cancellationTokenSource.Token),
            "Search" => service.Search("query", cancellationTokenSource.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operationName)),
        };

        await AssertTransportCancellation(operation, handler, cancellationTokenSource);
    }

    [TestCaseSource(nameof(s_libraryOperations))]
    public async Task LibraryOperations_PropagateCancellationToTransport(string operationName)
    {
        using var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        var service = new LibraryService(app);
        Package package = CreatePackage(app);

        Task operation = operationName switch
        {
            "GetPackage" => service.GetPackage("package-name", cancellationTokenSource.Token),
            "GetProfile" => service.GetProfile("profile-name", cancellationTokenSource.Token),
            "GetPackages" => service.GetPackages(cancellationTokenSource.Token),
            "Acquire" => service.Acquire(package, cancellationTokenSource.Token),
            "RemovePackage" => service.RemovePackage(package, cancellationTokenSource.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operationName)),
        };

        await AssertTransportCancellation(operation, handler, cancellationTokenSource);
    }

    [TestCaseSource(nameof(s_packageOperations))]
    public async Task PackageOperations_PropagateCancellationToTransport(string operationName)
    {
        using var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        Package package = CreatePackage(app);

        Task operation = operationName switch
        {
            "RefreshAsync" => package.RefreshAsync(cancellationTokenSource.Token),
            "GetReleaseAsync" => package.GetReleaseAsync("1.0.0", cancellationTokenSource.Token),
            "GetReleasesAsync" => package.GetReleasesAsync(cancellationTokenSource.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operationName)),
        };

        await AssertTransportCancellation(operation, handler, cancellationTokenSource);
    }

    [TestCaseSource(nameof(s_releaseOperations))]
    public async Task ReleaseOperations_PropagateCancellationToTransport(string operationName)
    {
        using var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        Release release = CreateRelease(app);

        Task operation = operationName switch
        {
            "RefreshAsync" => release.RefreshAsync(cancellationTokenSource.Token),
            "GetAssetAsync" => release.GetAssetAsync(cancellationTokenSource.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operationName)),
        };

        await AssertTransportCancellation(operation, handler, cancellationTokenSource);
    }

    [TestCaseSource(nameof(s_releaseOperations))]
    public async Task ReleaseOperations_LinkApplicationLifetimeToTransport(string operationName)
    {
        using var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        Release release = CreateRelease(app);

        Task operation = operationName switch
        {
            "RefreshAsync" => release.RefreshAsync(CancellationToken.None),
            "GetAssetAsync" => release.GetAssetAsync(CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operationName)),
        };

        await handler.BlockingRequestStarted.WaitAsync(TimeSpan.FromSeconds(5));
        app.Dispose();

        Assert.CatchAsync<OperationCanceledException>(async () => await operation);
        await handler.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DiscoverFeatured_PropagatesCancellationToPackageDetailRequests()
    {
        using var handler = new BlockingHandler(request =>
            request.RequestUri?.AbsolutePath == "/api/v3/discover/featured"
                ? JsonResponse(HttpStatusCode.OK, $"[{SimplePackageJson}]")
                : null);
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        var service = new DiscoverService(app);

        Task operation = service.GetFeatured(cancellationTokenSource.Token);

        await AssertTransportCancellation(operation, handler, cancellationTokenSource);
    }

    [Test]
    public async Task LibraryPackages_PropagatesCancellationToPackageDetailRequests()
    {
        using var handler = new BlockingHandler(request =>
            request.RequestUri?.AbsolutePath == "/api/v3/account/library"
                ? JsonResponse(HttpStatusCode.OK, $"[{{\"package\":{SimplePackageJson},\"latestRelease\":null}}]")
                : null);
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        var service = new LibraryService(app);

        Task operation = service.GetPackages(cancellationTokenSource.Token);

        await AssertTransportCancellation(operation, handler, cancellationTokenSource);
    }

    private const string SimplePackageJson = """
        {
          "id": "package-id",
          "owner": {
            "id": "profile-id",
            "name": "profile-name",
            "displayName": "Profile",
            "bio": null,
            "iconId": null,
            "iconUrl": null
          },
          "name": "package-name",
          "displayName": "Package",
          "shortDescription": "Package description",
          "tags": [],
          "logoId": null,
          "logoUrl": null,
          "currency": null,
          "price": null,
          "paid": false,
          "owned": false
        }
        """;

    private static void AssertRequiredCancellationTokens(Type type, IEnumerable<string> operationNames)
    {
        foreach (string operationName in operationNames)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == operationName)
                .ToArray();
            Assert.That(methods, Has.Length.EqualTo(1), $"{type.Name}.{operationName} must have one public signature.");

            ParameterInfo[] cancellationTokens = methods[0].GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(CancellationToken))
                .ToArray();
            Assert.That(
                cancellationTokens,
                Has.Length.EqualTo(1),
                $"{type.Name}.{operationName} must require a CancellationToken.");
            Assert.That(
                cancellationTokens[0].HasDefaultValue,
                Is.False,
                $"{type.Name}.{operationName} must not default its CancellationToken.");
        }
    }

    private static async Task AssertTransportCancellation(
        Task operation,
        BlockingHandler handler,
        CancellationTokenSource cancellationTokenSource)
    {
        await handler.BlockingRequestStarted.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationTokenSource.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await operation);
        await handler.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Package CreatePackage(BeutlApiApplication app)
    {
        ProfileResponse ownerResponse = new()
        {
            Id = "profile-id",
            Name = "profile-name",
            DisplayName = "Profile",
            Bio = null,
            IconId = null,
            IconUrl = null,
        };
        var owner = new Profile(ownerResponse, app);
        PackageResponse packageResponse = new()
        {
            Id = "package-id",
            Owner = ownerResponse,
            Name = "package-name",
            DisplayName = "Package",
            Description = "Package description",
            ShortDescription = "Package description",
            WebSite = "https://example.com",
            Tags = [],
            LogoId = null,
            LogoUrl = null,
            Screenshots = [],
            Currency = null,
            Price = null,
            Paid = false,
            Owned = false,
        };
        return new Package(owner, packageResponse, app);
    }

    private static Release CreateRelease(BeutlApiApplication app)
    {
        return new Release(CreatePackage(app), new ReleaseResponse
        {
            Id = "release-id",
            Version = "1.0.0",
            Title = "Release",
            Description = "Release description",
            TargetVersion = null,
            FileId = "asset-id",
            FileUrl = "https://example.com/asset",
        }, app);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class BlockingHandler(
        Func<HttpRequestMessage, HttpResponseMessage?>? immediateResponse = null) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _blockingRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task BlockingRequestStarted => _blockingRequestStarted.Task;

        public Task CancellationObserved => _cancellationObserved.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (immediateResponse?.Invoke(request) is { } response)
            {
                response.RequestMessage = request;
                return response;
            }

            _blockingRequestStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking request completed without cancellation.");
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _cancellationObserved.TrySetResult();
                }
            }
        }
    }
}
