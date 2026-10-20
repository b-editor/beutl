using System.Collections.Specialized;
using System.Net;
using System.Reactive;
using System.Reactive.Subjects;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.UnitTests.TestInfrastructure;
using Reactive.Bindings;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiJobMonitorTests
{
    [Test]
    public async Task PublishedSnapshot_IsBackedByReadOnlyReactiveAndImmutableJobState()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobMonitor service = app.GetResource<IAiJobMonitor>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                service.Snapshot,
                Is.Not.InstanceOf<ReactivePropertySlim<AiJobMonitorSnapshot>>());
            Assert.That(service.Snapshot.Value.Jobs.IsDefault, Is.False);
            Assert.That(service.Snapshot.Value.Jobs, Is.Empty);
        }
    }

    [Test]
    public async Task RefreshAsync_WhenSignedOut_PublishesAuthenticationStateWithoutRequest()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobMonitor service = app.GetResource<IAiJobMonitor>();

        await service.RefreshAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.Snapshot.Value.Jobs, Is.Empty);
            Assert.That(service.Snapshot.Value.IsLoading, Is.False);
            Assert.That(service.Snapshot.Value.Error, Is.TypeOf<AuthenticationRequiredException>());
            Assert.That(handler.Requests, Is.Empty);
        }
    }

    [Test]
    public async Task SigningOut_CancelsInFlightAuthenticationOwnedRefresh()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHttpHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.TrySetResult();
                }
            }

            return TestHttp.Json(HttpStatusCode.OK, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobMonitor service = app.GetResource<IAiJobMonitor>();

        ApiTestAuthentication.SignIn(app);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        app.SignOut(deleteFile: false);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Error is AuthenticationRequiredException,
            TimeSpan.FromSeconds(5));

        Assert.That(service.Snapshot.Value.Jobs, Is.Empty);
    }

    [Test]
    public async Task RefreshAsync_AppendsPageAndReplacesDuplicateWithNewestState()
    {
        using var handler = new DelegateHttpHandler(request =>
        {
            if (request.RequestUri?.Query.Contains("cursor=next-page", StringComparison.Ordinal) == true)
            {
                return TestHttp.Json(HttpStatusCode.OK, """
                    {
                      "jobs": [
                        {
                          "id": "job-1",
                          "kind": "video",
                          "status": "succeeded",
                          "inputParams": { "prompt": "First job", "durationSeconds": 6 },
                          "fileId": "file-1",
                          "url": "https://beutl.beditor.net/api/contents/file-1",
                          "usageUnits": 25,
                          "error": null,
                          "canRetry": false,
                          "createdAt": "2026-08-01T00:00:00Z",
                          "updatedAt": "2026-08-01T00:01:00Z"
                        },
                        {
                          "id": "job-2",
                          "kind": "image",
                          "status": "failed",
                          "inputParams": { "prompt": "Second job" },
                          "fileId": null,
                          "url": null,
                          "usageUnits": 20,
                          "error": "Provider failed",
                          "canRetry": true,
                          "createdAt": "2026-08-02T00:00:00Z",
                          "updatedAt": "2026-08-02T00:01:00Z"
                        }
                      ],
                      "nextCursor": null
                    }
                    """);
            }

            return TestHttp.Json(HttpStatusCode.OK, """
                {
                  "jobs": [
                    {
                      "id": "job-1",
                      "kind": "video",
                      "status": "running",
                      "inputParams": { "prompt": "First job", "durationSeconds": 6 },
                      "fileId": null,
                      "url": null,
                      "usageUnits": 25,
                      "error": null,
                      "canRetry": false,
                      "createdAt": "2026-08-01T00:00:00Z",
                      "updatedAt": "2026-08-01T00:00:30Z"
                    }
                  ],
                  "nextCursor": "next-page"
                }
                """);
        });
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobMonitor service = app.GetResource<IAiJobMonitor>();
        ApiTestAuthentication.SignIn(app);
        await service.RefreshAsync(CancellationToken.None);

        await service.LoadNextPageAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.Snapshot.Value.Jobs.Select(job => job.Id.Value),
                Is.EqualTo(new[] { "job-1", "job-2" }));
            Assert.That(service.Snapshot.Value.Jobs[0].Status, Is.EqualTo(AiJobStatuses.Succeeded));
            Assert.That(service.Snapshot.Value.NextCursor, Is.Null);
            Assert.That(service.Snapshot.Value.IsLoading, Is.False);
            Assert.That(service.Snapshot.Value.Error, Is.Null);
            Assert.That(handler.Requests.Any(uri => uri.Contains("cursor=next-page", StringComparison.Ordinal)),
                Is.True);
        }
    }

    [Test]
    public async Task RefreshAsync_WhenRequestFails_PreservesLastSuccessfulPage()
    {
        bool failRequests = false;
        using var handler = new DelegateHttpHandler(_ => failRequests
            ? TestHttp.Json(HttpStatusCode.InternalServerError, "{}")
            : TestHttp.Json(HttpStatusCode.OK, """
                {
                  "jobs": [
                    {
                      "id": "job-1",
                      "kind": "image",
                      "status": "succeeded",
                      "inputParams": { "prompt": "Keep me" },
                      "fileId": "file-1",
                      "url": "https://beutl.beditor.net/api/contents/file-1",
                      "usageUnits": 20,
                      "error": null,
                      "canRetry": false,
                      "createdAt": "2026-08-01T00:00:00Z",
                      "updatedAt": "2026-08-01T00:01:00Z"
                    }
                  ],
                  "nextCursor": "next-page"
                }
                """));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobMonitor service = app.GetResource<IAiJobMonitor>();
        ApiTestAuthentication.SignIn(app);
        await service.RefreshAsync(CancellationToken.None);
        failRequests = true;

        await service.RefreshAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.Snapshot.Value.Jobs.Select(job => job.Id.Value), Is.EqualTo(new[] { "job-1" }));
            Assert.That(service.Snapshot.Value.NextCursor, Is.EqualTo("next-page"));
            Assert.That(service.Snapshot.Value.IsLoading, Is.False);
            Assert.That(service.Snapshot.Value.Error, Is.Not.Null);
        }
    }

    [Test]
    public async Task InitialHistoryFailure_IsRetriedWithoutAnOpenJobCenter()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient();
        client.PageProvider = _ =>
        {
            if (Volatile.Read(ref client.PageRequests) <= 2)
                throw new HttpRequestException("transient");
            return new AiJobPage([], null);
        };
        var jobKinds = new AiJobKindRegistry([]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(app, client, jobKinds, changes, TimeSpan.FromMilliseconds(10));

        ApiTestAuthentication.SignIn(app);
        await TestWait.UntilAsync(() => Volatile.Read(ref client.PageRequests) >= 3, TimeSpan.FromSeconds(5));

        Assert.That(service.Snapshot.Value.Error, Is.Null);
    }

    [Test]
    public async Task PermanentInitialHistoryFailureIsNotRetriedInTheBackground()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient
        {
            PageProvider = _ => throw new InvalidDataException("invalid job contract"),
        };
        var jobKinds = new AiJobKindRegistry([]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromMilliseconds(10));

        ApiTestAuthentication.SignIn(app);
        await TestWait.UntilAsync(() => Volatile.Read(ref client.PageRequests) == 1, TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.That(client.PageRequests, Is.EqualTo(1));
        Assert.That(service.Snapshot.Value.Error, Is.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task RetryForANewAccountIsNotBlockedByThePreviousAccountsDelay()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient();
        client.PageProvider = _ => Volatile.Read(ref client.PageRequests) <= 2
            ? throw new HttpRequestException("transient")
            : new AiJobPage([], null);
        var firstDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDelayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int delayCount = 0;
        Task RetryDelay(TimeSpan _, CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref delayCount);
            if (current == 1)
            {
                firstDelayStarted.TrySetResult();
                return releaseFirstDelay.Task;
            }

            secondDelayStarted.TrySetResult();
            return releaseSecondDelay.Task;
        }
        var jobKinds = new AiJobKindRegistry([]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromSeconds(1),
            RetryDelay);

        ApiTestAuthentication.SignIn(app, "account-a");
        await firstDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ApiTestAuthentication.SignIn(app, "account-b");
        await secondDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseSecondDelay.TrySetResult();
        await TestWait.UntilAsync(() => Volatile.Read(ref client.PageRequests) >= 3, TimeSpan.FromSeconds(5));
        releaseFirstDelay.TrySetResult();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(delayCount, Is.EqualTo(2));
            Assert.That(service.Snapshot.Value.Error, Is.Null);
        }
    }

    [Test]
    public async Task PollingRefresh_RefreshesLoadedDepthInServerOrder()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        DateTimeOffset createdAt = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        AiJob first = Job("first", AiJobStatuses.Running, createdAt.AddMinutes(1));
        AiJob second = Job("second", AiJobStatuses.Running, createdAt);
        AiJob tailFirst = Job("tail-first", AiJobStatuses.Succeeded, createdAt.AddMinutes(-1));
        AiJob tailSecond = Job("tail-second", AiJobStatuses.Succeeded, createdAt.AddMinutes(-2));
        AiJob leading = Job("leading", AiJobStatuses.Queued, createdAt.AddMinutes(2));
        AiJob refreshedSecond = Job("second", AiJobStatuses.Succeeded, createdAt);
        AiJob refreshedFirst = Job("first", AiJobStatuses.Failed, createdAt.AddMinutes(1));
        int phase = 0;
        var client = new RecordingJobClient
        {
            PageProvider = request => (Volatile.Read(ref phase), request.Cursor) switch
            {
                (0, null) => new AiJobPage([first, second], "initial-tail"),
                (0, "initial-tail") => new AiJobPage([tailFirst, tailSecond], "initial-older"),
                (1, null) => new AiJobPage([leading, refreshedSecond], "refreshed-tail"),
                (1, "refreshed-tail") => new AiJobPage([refreshedFirst, tailFirst], "refreshed-older"),
                (2, null) => new AiJobPage([leading, refreshedSecond], null),
                _ => throw new InvalidOperationException(
                    $"Unexpected page request in phase {Volatile.Read(ref phase)}: {request.Cursor}"),
            },
        };
        var jobKinds = new AiJobKindRegistry([]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromHours(1));

        ApiTestAuthentication.SignIn(app);
        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Jobs.Select(job => job.Id.Value)
                .SequenceEqual(["first", "second"]),
            TimeSpan.FromSeconds(5));
        await service.LoadNextPageAsync(CancellationToken.None);
        Assert.That(
            service.Snapshot.Value.Jobs.Select(job => job.Id.Value),
            Is.EqualTo(new[] { "first", "second", "tail-first", "tail-second" }));

        int requestsBeforePolling = Volatile.Read(ref client.PageRequests);
        Volatile.Write(ref phase, 1);
        await service.RefreshPollingAsync(CancellationToken.None);

        AiJobMonitorSnapshot refreshed = service.Snapshot.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                refreshed.Jobs.Select(job => job.Id.Value),
                Is.EqualTo(new[] { "leading", "second", "first", "tail-first" }));
            Assert.That(refreshed.Jobs.Select(job => job.Id).Distinct().Count(), Is.EqualTo(4));
            Assert.That(refreshed.Jobs[0], Is.SameAs(leading));
            Assert.That(refreshed.Jobs[1], Is.SameAs(refreshedSecond));
            Assert.That(refreshed.Jobs[2], Is.SameAs(refreshedFirst));
            Assert.That(refreshed.Jobs[3], Is.SameAs(tailFirst));
            Assert.That(refreshed.Jobs, Does.Not.Contain(tailSecond));
            Assert.That(refreshed.NextCursor, Is.EqualTo("refreshed-older"));
            Assert.That(client.PageRequests - requestsBeforePolling, Is.EqualTo(2));
        }

        Volatile.Write(ref phase, 2);
        await service.RefreshAsync(CancellationToken.None);

        AiJobMonitorSnapshot replaced = service.Snapshot.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                replaced.Jobs.Select(job => job.Id.Value),
                Is.EqualTo(new[] { "leading", "second" }));
            Assert.That(replaced.Jobs[1], Is.SameAs(refreshedSecond));
            Assert.That(replaced.NextCursor, Is.Null);
            Assert.That(replaced.IsLoading, Is.False);
            Assert.That(replaced.Error, Is.Null);
        }

        static AiJob Job(string id, AiJobStatusId status, DateTimeOffset createdAt)
            => new(
                new AiJobId(id),
                AiJobKinds.Video,
                status,
                null,
                null,
                null,
                null,
                false,
                createdAt,
                createdAt);
    }

    [Test]
    public async Task PollingRefresh_DropsDeletedHeadJobsWhileKeepingRefreshedTail()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        DateTimeOffset createdAt = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
        AiJob active = Job("active", AiJobStatuses.Running, createdAt.AddMinutes(2));
        AiJob deleted = Job("deleted", AiJobStatuses.Succeeded, createdAt.AddMinutes(1));
        AiJob old = Job("old", AiJobStatuses.Succeeded, createdAt);
        AiJob refreshedActive = Job("active", AiJobStatuses.Succeeded, createdAt.AddMinutes(2));
        int phase = 0;
        var client = new RecordingJobClient
        {
            PageProvider = request => (Volatile.Read(ref phase), request.Cursor) switch
            {
                (0, null) => new AiJobPage([active, deleted], "initial-tail"),
                (0, "initial-tail") => new AiJobPage([old], null),
                (1, null) => new AiJobPage([refreshedActive], "refreshed-tail"),
                (1, "refreshed-tail") => new AiJobPage([old], null),
                _ => throw new InvalidOperationException(
                    $"Unexpected page request in phase {Volatile.Read(ref phase)}: {request.Cursor}"),
            },
        };
        var jobKinds = new AiJobKindRegistry([]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromHours(1));

        ApiTestAuthentication.SignIn(app);
        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Jobs.Select(job => job.Id.Value)
                .SequenceEqual(["active", "deleted"]),
            TimeSpan.FromSeconds(5));
        await service.LoadNextPageAsync(CancellationToken.None);

        int requestsBeforePolling = Volatile.Read(ref client.PageRequests);
        Volatile.Write(ref phase, 1);
        await service.RefreshPollingAsync(CancellationToken.None);

        AiJobMonitorSnapshot refreshed = service.Snapshot.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                refreshed.Jobs.Select(job => job.Id.Value),
                Is.EqualTo(new[] { "active", "old" }));
            Assert.That(refreshed.Jobs[0], Is.SameAs(refreshedActive));
            Assert.That(refreshed.Jobs[1], Is.SameAs(old));
            Assert.That(refreshed.Jobs, Does.Not.Contain(deleted));
            Assert.That(refreshed.NextCursor, Is.Null);
            Assert.That(client.PageRequests - requestsBeforePolling, Is.EqualTo(2));
        }

        static AiJob Job(string id, AiJobStatusId status, DateTimeOffset createdAt)
            => new(
                new AiJobId(id),
                AiJobKinds.Video,
                status,
                null,
                null,
                null,
                null,
                false,
                createdAt,
                createdAt);
    }

    [Test]
    public async Task JobChangeNotification_PreservesTheLoadedHistoryDepth()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        int phase = 0;
        var client = new RecordingJobClient
        {
            PageProvider = request => (Volatile.Read(ref phase), request.Cursor) switch
            {
                (0, null) => new AiJobPage([Job("head")], "tail"),
                (0, "tail") => new AiJobPage([Job("old-tail")], null),
                (1, null) => new AiJobPage([Job("new-head")], "new-tail"),
                (1, "new-tail") => new AiJobPage([Job("old-tail")], null),
                _ => throw new InvalidOperationException("Unexpected history page request."),
            },
        };
        var jobKinds = new AiJobKindRegistry([]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromHours(1));

        ApiTestAuthentication.SignIn(app);
        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Jobs.Select(job => job.Id.Value).SequenceEqual(["head"]),
            TimeSpan.FromSeconds(5));
        await service.LoadNextPageAsync(CancellationToken.None);
        int requestsBeforeNotification = Volatile.Read(ref client.PageRequests);

        Volatile.Write(ref phase, 1);
        changes.OnNext(Unit.Default);

        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Jobs.Select(job => job.Id.Value)
                .SequenceEqual(["new-head", "old-tail"]),
            TimeSpan.FromSeconds(5));
        Assert.That(client.PageRequests - requestsBeforeNotification, Is.EqualTo(2));

        static AiJob Job(string id) => new(
            new AiJobId(id),
            AiJobKinds.Image,
            AiJobStatuses.Succeeded,
            null,
            null,
            null,
            null,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task RefreshAsync_WhenUserSignsOutDuringRequest_DoesNotRestoreStaleJobs()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHttpHandler(async _ =>
        {
            requestStarted.TrySetResult();
            await releaseResponse.Task;
            return TestHttp.Json(HttpStatusCode.OK, """
                {
                  "jobs": [
                    {
                      "id": "stale-job",
                      "kind": "image",
                      "status": "succeeded",
                      "inputParams": { "prompt": "Must not reappear" },
                      "fileId": "file-1",
                      "url": "https://beutl.beditor.net/api/contents/file-1",
                      "usageUnits": 20,
                      "error": null,
                      "canRetry": false,
                      "createdAt": "2026-08-01T00:00:00Z",
                      "updatedAt": "2026-08-01T00:01:00Z"
                    }
                  ],
                  "nextCursor": null
                }
                """);
        });
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobMonitor service = app.GetResource<IAiJobMonitor>();

        ApiTestAuthentication.SignIn(app);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task queuedRefresh = service.RefreshAsync(CancellationToken.None);
        app.SignOut(deleteFile: false);
        releaseResponse.TrySetResult();
        await queuedRefresh.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.Snapshot.Value.Jobs, Is.Empty);
            Assert.That(service.Snapshot.Value.IsLoading, Is.False);
            Assert.That(service.Snapshot.Value.Error, Is.TypeOf<AuthenticationRequiredException>());
        }
    }

    [Test]
    public async Task ActiveJob_IsPolledToCompletionWithoutUiPollingLease()
    {
        int requestCount = 0;
        using var handler = new DelegateHttpHandler(_ =>
        {
            int current = Interlocked.Increment(ref requestCount);
            string status = current == 1 ? "queued" : "succeeded";
            return TestHttp.Json(HttpStatusCode.OK, $$"""
                {
                  "jobs": [
                    {
                      "id": "job-1",
                      "kind": "video",
                      "status": "{{status}}",
                      "inputParams": { "prompt": "A clip", "durationSeconds": 4 },
                      "fileId": {{(status == "succeeded" ? "\"file-1\"" : "null")}},
                      "url": {{(status == "succeeded" ? "\"https://example.com/video.mp4\"" : "null")}},
                      "usageUnits": 160,
                      "error": null,
                      "canRetry": false,
                      "createdAt": "2026-08-01T00:00:00Z",
                      "updatedAt": "2026-08-01T00:01:00Z"
                    }
                  ],
                  "nextCursor": null
                }
                """);
        });
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        using var service = new AiJobMonitor(
            app,
            app.GetResource<IAiJobClient>(),
            app.GetResource<IAiJobKindRegistry>(),
            app.GetResource<AiJobChangeNotifier>().Changes,
            TimeSpan.FromMilliseconds(10));

        ApiTestAuthentication.SignIn(app);

        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Jobs.SingleOrDefault()?.Status == AiJobStatuses.Succeeded,
            TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task PollingLease_PeriodicallyDiscoversJobsCreatedByAnotherClient()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient();
        client.PageProvider = _ => Volatile.Read(ref client.PageRequests) >= 3
            ? new AiJobPage([
                    new AiJob(
                        new AiJobId("external-job"),
                        AiJobKinds.Image,
                        AiJobStatuses.Succeeded,
                        null,
                        null,
                        null,
                        null,
                        false,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow),
                ], null)
            : new AiJobPage([], null);
        var jobKinds = new AiJobKindRegistry([]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromMilliseconds(10));

        ApiTestAuthentication.SignIn(app);
        await TestWait.UntilAsync(() => Volatile.Read(ref client.PageRequests) == 1, TimeSpan.FromSeconds(5));
        using IDisposable polling = service.AcquirePolling();

        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Jobs.SingleOrDefault()?.Id.Value == "external-job",
            TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task PollingLease_DoesNotRepeatHistoryRequestsAfterAuthenticationFailure()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient();
        client.PageProvider = _ => Volatile.Read(ref client.PageRequests) == 1
            ? new AiJobPage([], null)
            : throw new AuthenticationRequiredException();
        var jobKinds = new AiJobKindRegistry([]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromMilliseconds(10));

        ApiTestAuthentication.SignIn(app);
        await TestWait.UntilAsync(() => Volatile.Read(ref client.PageRequests) == 1, TimeSpan.FromSeconds(5));
        using IDisposable polling = service.AcquirePolling();
        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Error is AuthenticationRequiredException,
            TimeSpan.FromSeconds(5));
        int requestsAfterFailure = Volatile.Read(ref client.PageRequests);

        await Task.Delay(100);

        Assert.That(client.PageRequests, Is.EqualTo(requestsAfterFailure));
    }

    [Test]
    public async Task UnknownStatus_IsPublishedButDoesNotStartUnboundedPolling()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient
        {
            Page = new AiJobPage(
            [
                new AiJob(
                    new AiJobId("future-job"),
                    AiJobKinds.Video,
                    new AiJobStatusId("provider-paused"),
                    null,
                    null,
                    null,
                    null,
                    false,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
            ],
            null),
        };
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            app.GetResource<IAiJobKindRegistry>(),
            changes,
            TimeSpan.FromMilliseconds(10));

        ApiTestAuthentication.SignIn(app);
        await TestWait.UntilAsync(() => Volatile.Read(ref client.PageRequests) == 1, TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.PageRequests, Is.EqualTo(1));
            Assert.That(service.Snapshot.Value.Jobs.Single().Status.Value, Is.EqualTo("provider-paused"));
        }
    }

    [Test]
    public async Task CustomKindDescriptor_DrivesPollingWithoutBuiltInStatusesOrVideo()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        ApiTestAuthentication.SignIn(app);
        var client = new ExtensibleJobClient();
        var refreshHandler = new ExtensibleRefreshHandler(client);
        var kind = new AiJobKindId("vendor.render");
        var statusResolver = new AiJobStatusMap(
            [
                KeyValuePair.Create(
                    new AiJobStatusId("vendor-waiting"),
                    new AiJobStatusSemantics(false, true)),
                KeyValuePair.Create(
                    new AiJobStatusId("vendor-done"),
                    new AiJobStatusSemantics(
                        true,
                        false,
                        new AiJobOutcomeId("vendor.complete"))),
            ]);
        var jobKinds = new AiJobKindRegistry(
            [KeyValuePair.Create(kind, (IAiJobStatusResolver)statusResolver)],
            [KeyValuePair.Create(kind, (IAiJobRefreshHandler)refreshHandler)]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromMilliseconds(10));

        await TestWait.UntilAsync(
            () => service.Snapshot.Value.Jobs.SingleOrDefault()?.Status
                == new AiJobStatusId("vendor-done"),
            TimeSpan.FromSeconds(3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.Snapshot.Value.Error, Is.Null);
            Assert.That(jobKinds.GetStatus(service.Snapshot.Value.Jobs.Single()).Outcome,
                Is.EqualTo(new AiJobOutcomeId("vendor.complete")));
        }
    }

    [Test]
    public async Task ThrowingStatusResolver_DoesNotAbortMonitorPollingPredicates()
    {
        using var handler = new DelegateHttpHandler(_ => TestHttp.Json(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        ApiTestAuthentication.SignIn(app);
        var client = new RecordingJobClient
        {
            Page = new AiJobPage([
                new AiJob(
                    new AiJobId("throwing"),
                    new AiJobKindId("vendor.throwing"),
                    new AiJobStatusId("pending"),
                    null, null, null, null, false,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            ], null),
        };
        var jobKinds = new AiJobKindRegistry(
            [KeyValuePair.Create(new AiJobKindId("vendor.throwing"), (IAiJobStatusResolver)new ThrowingStatusResolver())]);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app, client, jobKinds, changes, TimeSpan.FromMilliseconds(10));

        await service.RefreshAsync(CancellationToken.None);
        using var polling = service.AcquirePolling();
        await Task.Delay(50);
    }

    [Test]
    public void StatusSemantics_KeepTerminalityPollingAndOpenOutcomeIndependent()
    {
        var resolver = new AiJobStatusMap(
        [
            KeyValuePair.Create(
                new AiJobStatusId("vendor-waiting"),
                new AiJobStatusSemantics(false, true)),
            KeyValuePair.Create(
                new AiJobStatusId("vendor-paused"),
                new AiJobStatusSemantics(false, false)),
            KeyValuePair.Create(
                new AiJobStatusId("vendor-finished"),
                new AiJobStatusSemantics(
                    true,
                    false,
                    new AiJobOutcomeId("vendor.review-required"))),
        ]);

        AiJobStatusSemantics waiting = resolver.Resolve(new AiJobStatusId("VENDOR-WAITING"));
        AiJobStatusSemantics paused = resolver.Resolve(new AiJobStatusId("vendor-paused"));
        AiJobStatusSemantics finished = resolver.Resolve(new AiJobStatusId("vendor-finished"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(waiting.IsTerminal, Is.False);
            Assert.That(waiting.ShouldPoll, Is.True);
            Assert.That(waiting.Outcome, Is.Null);
            Assert.That(paused.IsTerminal, Is.False);
            Assert.That(paused.ShouldPoll, Is.False);
            Assert.That(finished.IsTerminal, Is.True);
            Assert.That(finished.ShouldPoll, Is.False);
            Assert.That(finished.Outcome, Is.EqualTo(new AiJobOutcomeId("vendor.review-required")));
            Assert.That(
                resolver.Resolve(new AiJobStatusId("vendor-unknown")),
                Is.EqualTo(AiJobStatusSemantics.Unknown));
        }
    }

    [Test]
    public void UnregisteredKind_HasUnknownSemanticsAndNoBehaviors()
    {
        var registry = new AiJobKindRegistry([]);
        var job = new AiJob(
            new AiJobId("unknown-job"),
            new AiJobKindId("vendor.unknown"),
            new AiJobStatusId("mystery"),
            null,
            null,
            null,
            null,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.GetStatus(job), Is.EqualTo(AiJobStatusSemantics.Unknown));
            Assert.That(registry.TryGetRefreshHandler(job.Kind, out _), Is.False);
            Assert.That(registry.TryGetRetryHandler(job.Kind, out _), Is.False);
        }
    }

    private sealed class RecordingJobClient : IAiJobClient
    {
        public int PageRequests;

        public AiJobPage Page { get; init; } = new([], null);
        public Func<AiJobPageRequest, AiJobPage>? PageProvider { get; set; }

        public Task<AiJobPage> GetPageAsync(
            AiJobPageRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref PageRequests);
            return Task.FromResult(PageProvider?.Invoke(request) ?? Page);
        }

        public Task DeleteAsync(AiJobId jobId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class ExtensibleJobClient : IAiJobClient
    {
        private int _completed;

        public void Complete() => Volatile.Write(ref _completed, 1);

        public Task<AiJobPage> GetPageAsync(
            AiJobPageRequest request,
            CancellationToken cancellationToken)
        {
            AiJobStatusId status = Volatile.Read(ref _completed) == 0
                ? new AiJobStatusId("vendor-waiting")
                : new AiJobStatusId("vendor-done");
            var job = new AiJob(
                new AiJobId("vendor-job"),
                new AiJobKindId("vendor.render"),
                status,
                null,
                null,
                null,
                null,
                false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            return Task.FromResult(new AiJobPage([job], null));
        }

        public Task DeleteAsync(AiJobId jobId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class ExtensibleRefreshHandler(ExtensibleJobClient client) : IAiJobRefreshHandler
    {
        public Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
        {
            client.Complete();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingStatusResolver : IAiJobStatusResolver
    {
        public AiJobStatusSemantics Resolve(AiJobStatusId status)
            => throw new InvalidOperationException("status resolver failure");
    }
}
