using System.Net;
using System.Net.Http.Json;
using System.Text;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
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
    public async Task CompleteSignInAsync_DoesNotResurrectUser_WhenSignedOutDuringGetSelf()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await releaseRequest.Task;
            throw new HttpRequestException("request failed");
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var authResponse = new AuthResponse
        {
            Token = "new-token",
            RefreshToken = "new-refresh",
            Expiration = DateTime.UtcNow.AddHours(1)
        };

        Task<AuthenticatedUser> signIn = app.CompleteSignInAsync(authResponse, CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // SignOut while GetSelf is pending; the failing attempt must not resurrect the user.
        app.SignOut(false);
        releaseRequest.TrySetResult();

        Assert.CatchAsync<Exception>(async () =>
            await signIn.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(app.AuthenticatedUser.Value, Is.Null,
            "SignOut must not be undone by the failing sign-in");
    }

    [Test]
    public async Task CompleteSignInAsync_RestoresAuthorization_WhenCancelledAfterGetSelf()
    {
        var requestCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        using var handler = new DelegateHandler((request, cancellationToken) =>
        {
            requestCompleted.TrySetResult();
            // Cancel while the response is being processed, so the recheck after GetSelf
            // throws before the new token is committed.
            cancellation.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
            });
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var authResponse = new AuthResponse
        {
            Token = "new-token",
            RefreshToken = "new-refresh",
            Expiration = DateTime.UtcNow.AddHours(1)
        };

        Task<AuthenticatedUser> signIn = app.CompleteSignInAsync(authResponse, cancellation.Token);
        await requestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await signIn.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(httpClient.DefaultRequestHeaders.Authorization, Is.Null,
            "the uncommitted new token must not remain after a canceled re-sign-in");
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

    [Test]
    public async Task RestoreUserAsync_Rethrows_WhenCancelledAfterProfileRefresh()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        string userFile = Path.Combine(Helper.AppRoot, BeutlApiApplication.UserFileName);
        File.WriteAllText(userFile, """
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
            """);
        try
        {
            var requestCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            BeutlApiApplication? appRef = null;
            using var handler = new DelegateHandler((request, cancellationToken) =>
            {
                requestCompleted.TrySetResult();
                // Dispose while the profile response is being processed, so the lifetime
                // token is cancelled before RestoreUserAsync publishes the restored state.
                appRef?.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
            appRef = app;

            Task restore = app.RestoreUserAsync(null, CancellationToken.None);
            await requestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await restore.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            File.Delete(userFile);
        }
    }

    [Test]
    public async Task CompleteSignInAsync_Rethrows_WhenCancelledAfterProfileResponse()
    {
        var requestCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeutlApiApplication? appRef = null;
        using var handler = new DelegateHandler((request, cancellationToken) =>
        {
            requestCompleted.TrySetResult();
            // Dispose while the profile response is being processed, so the lifetime token
            // is cancelled before CompleteSignInAsync publishes the signed-in state.
            appRef?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
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
            });
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        appRef = app;
        var authResponse = new AuthResponse
        {
            Token = "new-token",
            RefreshToken = "new-refresh",
            Expiration = DateTime.UtcNow.AddHours(1)
        };

        using CancellationTokenSource lifetimeCts = app.CreateLifetimeLinkedTokenSource(CancellationToken.None);
        Task<AuthenticatedUser> signIn = app.CompleteSignInAsync(authResponse, lifetimeCts.Token);
        await requestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await signIn.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task ReadUserAsync_PreCanceledRequestStopsBeforeFileAccess()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await app.ReadUserAsync(cancellationTokenSource.Token));
    }

    [Test]
    public async Task Dispose_IsIdempotentAndRejectsFurtherResourceResolution()
    {
        using var httpClient = new HttpClient();
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        _ = app.GetResource<PackageManager>();

        Assert.DoesNotThrowAsync(async () => await app.DisposeAsync());
        Assert.DoesNotThrowAsync(async () => await app.DisposeAsync());
        Assert.Throws<ObjectDisposedException>(() => app.GetResource<DiscoverService>());
    }

    [Test]
    public async Task CheckForUpdatesAsync_Rethrows_WhenCancelledAfterResponse()
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
                  "type": "zip"
                }
                """);
            var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new DelegateHandler(async (request, cancellationToken) =>
            {
                requestStarted.TrySetResult();
                await releaseResponse.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"latestVersion":"2.0.0-preview.7","url":"https://example.com","downloadUrl":null,"isLatest":false,"mustLatest":false}""",
                        Encoding.UTF8,
                        "application/json")
                };
            });
            using var httpClient = new HttpClient(handler);
            var app = new BeutlApiApplication(httpClient, new ExtensionProvider());

            // Dispose after the response completes but before the continuation runs: the
            // lifetime token is cancelled, so the method must recheck it before returning.
            Task<(CheckForUpdatesResponse? V1, AppUpdateResponse? V3)> check
                = app.CheckForUpdatesAsync("2.0.0-preview.6", CancellationToken.None);
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await app.DisposeAsync().AsTask();
            releaseResponse.TrySetResult();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await check.WaitAsync(TimeSpan.FromSeconds(5)));
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

    [Test]
    public async Task RemovePackage_Rethrows_WhenCancelledAfterDelete()
    {
        var requestCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeutlApiApplication? appRef = null;
        using var handler = new DelegateHandler((request, cancellationToken) =>
        {
            requestCompleted.TrySetResult();
            // Dispose while the delete response is being processed, so the lifetime token
            // is cancelled before RemovePackage returns.
            appRef?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        appRef = app;
        var library = app.GetResource<LibraryService>();
        var owner = new Profile(CreateProfileResponse(), app);
        var package = new Package(owner, CreatePackageResponse(), app);

        Task remove = library.RemovePackage(package, CancellationToken.None);
        await requestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await remove.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task DisposeAsync_ReentrantCallback_ObservesTheOriginalTeardown()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        _ = app.GetResource<PackageManager>();

        Task? reentrantTask = null;
        using CancellationTokenSource linked = app.CreateLifetimeLinkedTokenSource(CancellationToken.None);
        using var registration = linked.Token.Register(() =>
        {
            // A cancellation callback that re-enters DisposeAsync must observe the
            // original disposal task instead of starting a second pipeline.
            reentrantTask = app.DisposeAsync().AsTask();
        });

        Task outer = app.DisposeAsync().AsTask();
        await outer.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(reentrantTask, Is.Not.Null);
        Assert.That(reentrantTask, Is.SameAs(outer),
            "the reentrant call must observe the original disposal task");
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }

    private static ProfileResponse CreateProfileResponse()
    {
        return new ProfileResponse
        {
            Id = "profile-id",
            Name = "profile-name",
            DisplayName = "Profile Name",
            Bio = null,
            IconId = null,
            IconUrl = null,
        };
    }

    private static PackageResponse CreatePackageResponse()
    {
        return new PackageResponse
        {
            Id = "package-id",
            Owner = CreateProfileResponse(),
            Name = "package-name",
            DisplayName = "Package Name",
            Description = "Description",
            ShortDescription = "Short description",
            WebSite = null,
            Tags = [],
            LogoId = null,
            LogoUrl = null,
            Screenshots = [],
            Currency = null,
            Price = null,
            Paid = false,
            Owned = false,
        };
    }
}
