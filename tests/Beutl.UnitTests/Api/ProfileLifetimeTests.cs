using System.Net;
using System.Net.Http.Json;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.UnitTests.TestInfrastructure;

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
        var profile = new Profile(ApiTestAuthentication.CreateProfileResponse(), app);

        Task refresh = profile.RefreshAsync(CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.CatchAsync<OperationCanceledException>(async () =>
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
        var profile = new Profile(ApiTestAuthentication.CreateProfileResponse(), app);

        Task getPackages = profile.GetPackagesAsync(CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.CatchAsync<OperationCanceledException>(async () =>
            await getPackages.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task RefreshAsync_DoesNotPublishState_WhenCancelledAfterResponse()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHttpHandler(async (request, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await releaseRequest.Task;
            // Return a valid, different profile even though the token was cancelled; the method must
            // recheck the lifetime token before publishing the refreshed state.
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ProfileResponse
                {
                    Id = "test-user",
                    Name = "test",
                    DisplayName = "Refreshed",
                    Bio = null,
                    IconId = null,
                    IconUrl = null,
                })
            };
        });
        using var httpClient = new HttpClient(handler);
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var profile = new Profile(ApiTestAuthentication.CreateProfileResponse(), app);

        Task refresh = profile.RefreshAsync(CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        releaseRequest.TrySetResult();

        await Assert.CatchAsync<OperationCanceledException>(async () =>
            await refresh.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(profile.DisplayName.Value, Is.EqualTo("Test User"));
    }

    [Test]
    public async Task GetPackagesAsync_Rethrows_WhenCancelledAfterEmptyResponse()
    {
        var requestCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeutlApiApplication? appRef = null;
        using var handler = new DelegateHttpHandler((request, cancellationToken) =>
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
        var profile = new Profile(ApiTestAuthentication.CreateProfileResponse(), app);

        Task<Package[]> getPackages = profile.GetPackagesAsync(CancellationToken.None);
        await requestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.CatchAsync<OperationCanceledException>(async () =>
            await getPackages.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task RefreshAsync_PreCanceledToken_Throws()
    {
        using var httpClient = new HttpClient();
        var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        AuthenticatedUser user = ApiTestAuthentication.CreateUser(app);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(async () =>
            await user.RefreshAsync(cancellation.Token));
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
}
