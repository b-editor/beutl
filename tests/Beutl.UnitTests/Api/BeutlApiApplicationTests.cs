using System.Net;
using System.Net.Http.Json;
using System.Text;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Testing.Headless;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class BeutlApiApplicationTests
{
    [Test]
    public void Constructor_RegistersProvidedExtensionProvider()
    {
        using var httpClient = new HttpClient();
        var extensionProvider = new ExtensionProvider();
        var app = new BeutlApiApplication(httpClient, extensionProvider);

        ExtensionProvider registeredProvider = app.GetResource<ExtensionProvider>();
        PackageManager packageManager = app.GetResource<PackageManager>();

        Assert.That(registeredProvider, Is.SameAs(extensionProvider));
        Assert.That(packageManager.ExtensionProvider, Is.SameAs(extensionProvider));
    }

    [Test]
    public void Constructor_NullHttpClient_Throws()
    {
        var extensionProvider = new ExtensionProvider();

        Assert.Throws<ArgumentNullException>(() => _ = new BeutlApiApplication(null!, extensionProvider));
    }

    [Test]
    public void Constructor_NullExtensionProvider_Throws()
    {
        using var httpClient = new HttpClient();

        Assert.Throws<ArgumentNullException>(() => _ = new BeutlApiApplication(httpClient, null!));
    }

    [Test]
    public void ToServerType_Flatpak_MapsToZip()
    {
        Assert.That(BeutlApiApplication.ToServerType("flatpak"), Is.EqualTo("zip"));
    }

    [TestCase("zip")]
    [TestCase("debian")]
    [TestCase("installer")]
    [TestCase("app")]
    public void ToServerType_KnownType_PassesThrough(string type)
    {
        Assert.That(BeutlApiApplication.ToServerType(type), Is.EqualTo(type));
    }

    [Test]
    public async Task CheckForUpdatesAsync_WithFlatpakMetadata_SendsZipType()
    {
        // LoadMetadata reads asset_metadata.json from AppContext.BaseDirectory (cached per process).
        string metadataPath = Path.Combine(AppContext.BaseDirectory, "asset_metadata.json");
        string? originalContent = File.Exists(metadataPath) ? File.ReadAllText(metadataPath) : null;
        try
        {
            File.WriteAllText(metadataPath, """
                {
                  "id": "test-id",
                  "os": "linux",
                  "arch": "x64",
                  "version": "2.0.0-preview.6",
                  "standalone": "true",
                  "type": "flatpak"
                }
                """);
            var handler = new CapturingHandler();
            using var httpClient = new HttpClient(handler);
            var app = new BeutlApiApplication(httpClient, new ExtensionProvider());

            var (v1, v3) = await app.CheckForUpdatesAsync("2.0.0-preview.6", CancellationToken.None);

            Assert.That(handler.LastRequestUri, Is.Not.Null);
            Assert.That(handler.LastRequestUri!.Query, Does.Contain("type=zip"));
            Assert.That(handler.LastRequestUri!.Query, Does.Not.Contain("type=flatpak"));
            Assert.That(v3, Is.Not.Null);
            Assert.That(v1, Is.Null);
        }
        finally
        {
            if (originalContent != null)
            {
                File.WriteAllText(metadataPath, originalContent);
            }
            else
            {
                File.Delete(metadataPath);
            }
        }
    }

    [Test]
    public async Task CheckForUpdatesAsync_PreCanceledTokenStopsBeforeMetadataRead()
    {
        // LoadMetadata reads asset_metadata.json from AppContext.BaseDirectory (cached per process).
        string metadataPath = Path.Combine(AppContext.BaseDirectory, "asset_metadata.json");
        string? originalContent = File.Exists(metadataPath) ? File.ReadAllText(metadataPath) : null;
        try
        {
            File.WriteAllText(metadataPath, """
                {
                  "id": "test-id",
                  "os": "linux",
                  "arch": "x64",
                  "version": "2.0.0-preview.6",
                  "standalone": "true",
                  "type": "flatpak"
                }
                """);
            using var httpClient = new HttpClient();
            var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await app.CheckForUpdatesAsync("2.0.0-preview.6", cancellationTokenSource.Token));
        }
        finally
        {
            if (originalContent != null)
            {
                File.WriteAllText(metadataPath, originalContent);
            }
            else
            {
                File.Delete(metadataPath);
            }
        }
    }

    [Test]
    public async Task CompleteSignInAsync_RestoresAuthorization_WhenGetSelfIsCanceled()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        using var handler = new DelegateHandler((request, cancellationToken) =>
        {
            cancellationTokenSource.Cancel();
            throw new OperationCanceledException(cancellationToken);
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var authResponse = new AuthResponse
        {
            Token = "new-token",
            RefreshToken = "new-refresh",
            Expiration = DateTime.UtcNow.AddHours(1)
        };

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await app.CompleteSignInAsync(authResponse, cancellationTokenSource.Token));

        Assert.That(httpClient.DefaultRequestHeaders.Authorization, Is.Null,
            "the new bearer token must not remain after a canceled sign-in");
    }

    [Test]
    public async Task CompleteSignInAsync_RestoresAuthenticatedUser_WhenPersistenceFails()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        string userFile = Path.Combine(Helper.AppRoot, BeutlApiApplication.UserFileName);
        Directory.CreateDirectory(userFile); // SaveUser's File.Create fails on a directory.
        try
        {
            using var handler = new DelegateHandler((request, cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ProfileResponse
                    {
                        Id = "new-profile",
                        Name = "new-name",
                        DisplayName = "New Name",
                        Bio = null,
                        IconId = null,
                        IconUrl = null,
                    })
                }));
            using var httpClient = new HttpClient(handler);
            var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
            var authResponse = new AuthResponse
            {
                Token = "new-token",
                RefreshToken = "new-refresh",
                Expiration = DateTime.UtcNow.AddHours(1)
            };

            Assert.CatchAsync<Exception>(async () =>
                await app.CompleteSignInAsync(authResponse, CancellationToken.None));

            Assert.That(app.AuthenticatedUser.Value, Is.Null,
                "the failed sign-in must not leave a signed-in user");
            Assert.That(httpClient.DefaultRequestHeaders.Authorization, Is.Null,
                "the new bearer token must not remain after a failed sign-in");
        }
        finally
        {
            Directory.Delete(userFile, recursive: true);
        }
    }

     [Test]
     public async Task RestoreUserAsync_RestoresAuthorization_WhenProfileRefreshIsCanceled()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        string userFile = Path.Combine(Helper.AppRoot, BeutlApiApplication.UserFileName);
        string original = """
            {
              "token": "restored-token",
              "refresh_token": "restored-refresh",
              "expiration": "2027-01-01T00:00:00Z",
              "profile": {
                "id": "restored-profile",
                "name": "restored-name",
                "displayName": "Restored Name",
                "bio": null,
                "iconId": null,
                "iconUrl": null
              }
            }
            """;
        File.WriteAllText(userFile, original);
        try
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            using var handler = new DelegateHandler((request, cancellationToken) =>
            {
                if (request.RequestUri!.PathAndQuery.StartsWith("/api/v3/user"))
                {
                    cancellationTokenSource.Cancel();
                    throw new OperationCanceledException(cancellationToken);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ProfileResponse
                    {
                        Id = "restored-profile",
                        Name = "restored-name",
                        DisplayName = "Restored Name",
                        Bio = null,
                        IconId = null,
                        IconUrl = null,
                    })
                });
            });
            using var httpClient = new HttpClient(handler);
            var app = new BeutlApiApplication(httpClient, new ExtensionProvider());

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await app.RestoreUserAsync(null, cancellationTokenSource.Token));

            Assert.That(httpClient.DefaultRequestHeaders.Authorization, Is.Null,
                "the restored bearer token must not remain after a canceled restoration");
            Assert.That(app.AuthenticatedUser.Value, Is.Null,
                "the restored user must not remain after a canceled restoration");
        }
        finally
        {
            File.Delete(userFile);
        }
    }

     private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"latestVersion":"2.0.0-preview.7","url":"https://github.com/b-editor/beutl/releases/tag/v2.0.0-preview.7","downloadUrl":null,"isLatest":false,"mustLatest":false}
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
