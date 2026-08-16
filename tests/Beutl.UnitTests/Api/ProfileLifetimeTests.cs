using System.Net;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;

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
}
