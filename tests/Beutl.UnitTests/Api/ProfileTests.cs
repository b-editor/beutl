using System.Net;
using System.Reflection;
using System.Text;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class ProfileTests
{
    [Test]
    public async Task RefreshSelf_UsesCapturedBearerAndCommitsOnlyCompletedResponse()
    {
        string? authorization = null;
        using var handler = new StubHandler((request, _) =>
        {
            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {
                  "id": "test-user",
                  "name": "updated-name",
                  "displayName": "Updated User",
                  "bio": null,
                  "iconId": null,
                  "iconUrl": null
                }
                """));
        });
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        Profile profile = SetAuthenticatedUser(app);

        await profile.RefreshAsync(CancellationToken.None, self: true);

        Assert.Multiple(() =>
        {
            Assert.That(authorization, Is.EqualTo("Bearer token"));
            Assert.That(profile.Name, Is.EqualTo("updated-name"));
            Assert.That(profile.DisplayName.Value, Is.EqualTo("Updated User"));
        });
    }

    [Test]
    public async Task RefreshSelf_CancellationDoesNotMutateProfile()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        Profile profile = SetAuthenticatedUser(app);
        using var cancellationTokenSource = new CancellationTokenSource();

        Task refresh = profile.RefreshAsync(cancellationTokenSource.Token, self: true);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationTokenSource.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await refresh);
        Assert.Multiple(() =>
        {
            Assert.That(profile.Name, Is.EqualTo("test"));
            Assert.That(profile.DisplayName.Value, Is.EqualTo("Test User"));
        });
    }

    [Test]
    public async Task ConcurrentRefreshes_CannotCommitAnOlderResponseLast()
    {
        int requestCount = 0;
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            int request = Interlocked.Increment(ref requestCount);
            if (request == 1)
            {
                firstRequestStarted.TrySetResult();
                return await firstResponse.Task.WaitAsync(cancellationToken);
            }

            secondRequestStarted.TrySetResult();
            return await secondResponse.Task.WaitAsync(cancellationToken);
        });
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var profile = new Profile(CreateProfileResponse("test", "Original"), app);

        Task olderRefresh = profile.RefreshAsync(CancellationToken.None);
        await firstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task newerRefresh = profile.RefreshAsync(CancellationToken.None);
        await secondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        secondResponse.TrySetResult(JsonResponse(HttpStatusCode.OK, ProfileJson("newest", "Newest")));
        await newerRefresh;
        firstResponse.TrySetResult(JsonResponse(HttpStatusCode.OK, ProfileJson("older", "Older")));
        await olderRefresh;

        Assert.Multiple(() =>
        {
            Assert.That(profile.Name, Is.EqualTo("newest"));
            Assert.That(profile.DisplayName.Value, Is.EqualTo("Newest"));
        });
    }

    [Test]
    public async Task GetPackagesAsync_CancelsEveryPackageDetailRequest()
    {
        var detailRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detailCancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/v3/users/test/packages")
            {
                return JsonResponse(HttpStatusCode.OK, $$"""
                    [
                      {
                        "id": "package-id",
                        "owner": {{ProfileJson("test", "Test User")}},
                        "name": "package-name",
                        "displayName": "Package",
                        "shortDescription": "Package",
                        "tags": [],
                        "logoId": null,
                        "logoUrl": null,
                        "currency": null,
                        "price": null,
                        "paid": false,
                        "owned": false
                      }
                    ]
                    """);
            }

            detailRequestStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    detailCancellationObserved.TrySetResult();
                }
            }

            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var httpClient = new HttpClient(handler);
        using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var profile = new Profile(CreateProfileResponse("test", "Test User"), app);
        using var cancellationTokenSource = new CancellationTokenSource();

        Task operation = profile.GetPackagesAsync(cancellationTokenSource.Token);
        await detailRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellationTokenSource.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () => await operation);
        await detailCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Profile SetAuthenticatedUser(BeutlApiApplication app)
    {
        var profile = new Profile(CreateProfileResponse("test", "Test User"), app);
        var user = new AuthenticatedUser(profile, new AuthResponse
        {
            Token = "token",
            RefreshToken = "refresh-token",
            Expiration = DateTime.UtcNow.AddHours(1),
        }, app, DateTime.UtcNow);
        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        ((ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(app)!).Value = user;
        return profile;
    }

    private static ProfileResponse CreateProfileResponse(string name, string displayName)
        => new()
        {
            Id = "test-user",
            Name = name,
            DisplayName = displayName,
            Bio = null,
            IconId = null,
            IconUrl = null,
        };

    private static string ProfileJson(string name, string displayName)
        => $$"""
            {
              "id": "test-user",
              "name": "{{name}}",
              "displayName": "{{displayName}}",
              "bio": null,
              "iconId": null,
              "iconUrl": null
            }
            """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await responder(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }
}
