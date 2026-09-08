using System.Net;
using System.Net.Http.Json;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Testing.Headless;

namespace Beutl.UnitTests.Api;

[TestFixture]
[NonParallelizable]
public sealed class ProfileLifetimeTests
{
    [Test]
    public async Task RefreshAsync_IsCancelledByApplicationDisposal()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new BlockingHandler(requestStarted, releaseRequest);
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var profile = new Profile(CreateProfileResponse(), app);

        Task refresh = profile.RefreshAsync(CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task GetPackagesAsync_IsCancelledByApplicationDisposal()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new BlockingHandler(requestStarted, releaseRequest);
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var profile = new Profile(CreateProfileResponse(), app);

        Task getPackages = profile.GetPackagesAsync(CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await getPackages.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task RefreshAsync_DoesNotPublishState_WhenCancelledAfterResponse()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHandler(async (request, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await releaseRequest.Task;
            // Return a valid response even though the token was cancelled; the method must
            // recheck the lifetime token before publishing the refreshed state.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateProfileResponse())
            };
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var profile = new Profile(CreateProfileResponse(), app);

        Task refresh = profile.RefreshAsync(CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        releaseRequest.TrySetResult();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task GetPackagesAsync_Rethrows_WhenCancelledAfterEmptyResponse()
    {
        var requestCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeutlApiApplication? appRef = null;
        using var handler = new DelegateHandler((request, cancellationToken) =>
        {
            requestCompleted.TrySetResult();
            // Dispose the application while the empty response is being processed, so the
            // lifetime token is cancelled before GetPackagesAsync returns its final array.
            appRef?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        appRef = app;
        var profile = new Profile(CreateProfileResponse(), app);

        Task<Package[]> getPackages = profile.GetPackagesAsync(CancellationToken.None);
        await requestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await getPackages.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task AuthenticatedUserRefresh_Rethrows_WhenCancelledAfterFileRead()
    {
        Assert.That(Helper.AppRoot, Is.EqualTo(BeutlHomeIsolation.CurrentHome));
        string userFile = Path.Combine(Helper.AppRoot, BeutlApiApplication.UserFileName);
        File.WriteAllText(userFile, """
            {
              "token": "stale-token",
              "refresh_token": "stale-refresh",
              "expiration": "2027-01-01T00:00:00Z",
              "profile": {
                "id": "profile-id",
                "name": "profile-name",
                "displayName": "Profile Name",
                "bio": null,
                "iconId": null,
                "iconUrl": null
              }
            }
            """);
        try
        {
            using var handler = new DelegateHandler((request, cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                }));
            using var httpClient = new HttpClient(handler);
            var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
            var profile = new Profile(CreateProfileResponse(), app);
            var authResponse = new AuthResponse
            {
                Token = "stale-token",
                RefreshToken = "stale-refresh",
                Expiration = DateTime.UtcNow.AddDays(1)
            };
            var user = new AuthenticatedUser(profile, authResponse, app, httpClient, DateTime.UtcNow.AddDays(-1));

            // A stale write-time forces the persisted-file branch; a pre-canceled token
            // makes the cancellation point deterministic instead of racing the file read.
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Task refresh = user.RefreshAsync(cancellation.Token).AsTask();

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            File.Delete(userFile);
        }
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

    private sealed class BlockingHandler(
        TaskCompletionSource requestStarted,
        TaskCompletionSource releaseRequest) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requestStarted.TrySetResult();
            await releaseRequest.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
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
