using System.Net;
using System.Reactive;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiJobMonitorTests
{
    [Test]
    public async Task PublishedSnapshot_IsBackedByReadOnlyReactiveAndImmutableJobState()
    {
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
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
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
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
        using var handler = new StubHandler(async (_, cancellationToken) =>
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

            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        IAiJobMonitor service = app.GetResource<IAiJobMonitor>();

        SetAuthenticatedUser(app, httpClient);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        app.SignOut(deleteFile: false);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(
            () => service.Snapshot.Value.Error is AuthenticationRequiredException,
            TimeSpan.FromSeconds(5));

        Assert.That(service.Snapshot.Value.Jobs, Is.Empty);
    }

    [Test]
    public async Task RefreshAsync_AppendsPageAndReplacesDuplicateWithNewestState()
    {
        using var handler = new StubHandler(request =>
        {
            if (request.RequestUri?.Query.Contains("cursor=next-page", StringComparison.Ordinal) == true)
            {
                return JsonResponse(HttpStatusCode.OK, """
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

            return JsonResponse(HttpStatusCode.OK, """
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
        SetAuthenticatedUser(app, httpClient);
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
        using var handler = new StubHandler(_ => failRequests
            ? JsonResponse(HttpStatusCode.InternalServerError, "{}")
            : JsonResponse(HttpStatusCode.OK, """
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
        SetAuthenticatedUser(app, httpClient);
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
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient();
        client.PageProvider = _ =>
        {
            if (Volatile.Read(ref client.PageRequests) <= 2)
                throw new HttpRequestException("transient");
            return new AiJobPage([], null);
        };
        await using var jobKinds = new AiJobKindRegistry();
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(app, client, jobKinds, changes, TimeSpan.FromMilliseconds(10));

        SetAuthenticatedUser(app, httpClient);
        await WaitUntilAsync(() => Volatile.Read(ref client.PageRequests) >= 3, TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.PageRequests, Is.GreaterThanOrEqualTo(3));
            Assert.That(service.Snapshot.Value.Error, Is.Null);
        }
    }

    [Test]
    public async Task PermanentInitialHistoryFailureIsNotRetriedInTheBackground()
    {
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient
        {
            PageProvider = _ => throw new InvalidDataException("invalid job contract"),
        };
        await using var jobKinds = new AiJobKindRegistry();
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromMilliseconds(10));

        SetAuthenticatedUser(app, httpClient);
        await WaitUntilAsync(() => Volatile.Read(ref client.PageRequests) == 1, TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.That(client.PageRequests, Is.EqualTo(1));
        Assert.That(service.Snapshot.Value.Error, Is.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task RetryForANewAccountIsNotBlockedByThePreviousAccountsDelay()
    {
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
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
        await using var jobKinds = new AiJobKindRegistry();
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromSeconds(1),
            RetryDelay);

        SetAuthenticatedUser(app, httpClient, "account-a");
        await firstDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        SetAuthenticatedUser(app, httpClient, "account-b");
        await secondDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseSecondDelay.TrySetResult();
        await WaitUntilAsync(() => Volatile.Read(ref client.PageRequests) >= 3, TimeSpan.FromSeconds(5));
        releaseFirstDelay.TrySetResult();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(delayCount, Is.EqualTo(2));
            Assert.That(service.Snapshot.Value.Error, Is.Null);
        }
    }

    [Test]
    public async Task PollingRefresh_PreservesPreviouslyLoadedHistoryTail()
    {
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var client = new RecordingJobClient();
        bool pollingRefresh = false;
        client.PageProvider = request => request.Cursor == "tail"
            ? new AiJobPage([
                Job("active", AiJobStatuses.Running),
                Job("tail", AiJobStatuses.Succeeded),
            ], null)
            : new AiJobPage([
                Job("active", pollingRefresh ? AiJobStatuses.Succeeded : AiJobStatuses.Running),
            ], pollingRefresh ? null : "tail");
        await using var jobKinds = new AiJobKindRegistry();
        await using IAiJobKindRegistration registration = jobKinds.Register(
            new AiJobKindDescriptor(
                new AiJobKindId("video"),
                new AiJobStatusMap([
                    KeyValuePair.Create(AiJobStatuses.Running, new AiJobStatusSemantics(false, true)),
                    KeyValuePair.Create(AiJobStatuses.Succeeded, new AiJobStatusSemantics(true, false)),
                ])));
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(app, client, jobKinds, changes, TimeSpan.FromMilliseconds(10));

        SetAuthenticatedUser(app, httpClient);
        await WaitUntilAsync(() => client.PageRequests >= 1, TimeSpan.FromSeconds(5));
        await service.LoadNextPageAsync(CancellationToken.None);
        Assert.That(service.Snapshot.Value.Jobs.Select(job => job.Id.Value), Is.EqualTo(new[] { "active", "tail" }));

        pollingRefresh = true;
        await service.RefreshPollingAsync(CancellationToken.None);
        Assert.That(service.Snapshot.Value.Jobs.Select(job => job.Id.Value), Is.EqualTo(new[] { "active", "tail" }));

        static AiJob Job(string id, AiJobStatusId status)
            => new(new AiJobId(id), new AiJobKindId("video"), status,
                null, null, null, null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task RefreshAsync_WhenUserSignsOutDuringRequest_DoesNotRestoreStaleJobs()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async _ =>
        {
            requestStarted.TrySetResult();
            await releaseResponse.Task;
            return JsonResponse(HttpStatusCode.OK, """
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

        SetAuthenticatedUser(app, httpClient);
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
        using var handler = new StubHandler(_ =>
        {
            int current = Interlocked.Increment(ref requestCount);
            string status = current == 1 ? "queued" : "succeeded";
            return JsonResponse(HttpStatusCode.OK, $$"""
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

        SetAuthenticatedUser(app, httpClient);

        await WaitUntilAsync(
            () => service.Snapshot.Value.Jobs.SingleOrDefault()?.Status == AiJobStatuses.Succeeded,
            TimeSpan.FromSeconds(3));
        Assert.That(requestCount, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task UnknownStatus_IsPublishedButDoesNotStartUnboundedPolling()
    {
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
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

        SetAuthenticatedUser(app, httpClient);
        await WaitUntilAsync(() => Volatile.Read(ref client.PageRequests) == 1, TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(client.PageRequests, Is.EqualTo(1));
            Assert.That(service.Snapshot.Value.Jobs.Single().Status.Value, Is.EqualTo("provider-paused"));
        }
    }

    [Test]
    public async Task InjectedTransportAndJobChangeStream_AreIndependentSubstitutionSeams()
    {
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(app, httpClient);
        var client = new RecordingJobClient();
        await using var jobKinds = new AiJobKindRegistry();
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromHours(1));

        await WaitUntilAsync(() => Volatile.Read(ref client.PageRequests) >= 1, TimeSpan.FromSeconds(5));
        int beforeNotification = Volatile.Read(ref client.PageRequests);
        changes.OnNext(Unit.Default);
        await WaitUntilAsync(
            () => Volatile.Read(ref client.PageRequests) > beforeNotification,
            TimeSpan.FromSeconds(5));

        Assert.That(client.PageRequests, Is.GreaterThan(beforeNotification));
    }

    [Test]
    public async Task CustomKindDescriptor_DrivesPollingWithoutBuiltInStatusesOrVideo()
    {
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(app, httpClient);
        var client = new ExtensibleJobClient();
        await using var jobKinds = new AiJobKindRegistry();
        var refreshHandler = new ExtensibleRefreshHandler(client);
        var descriptor = new AiJobKindDescriptor(
            new AiJobKindId("vendor.render"),
            new AiJobStatusMap(
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
            ]))
        {
            RefreshHandler = refreshHandler,
        };
        await using IAiJobKindRegistration registration = jobKinds.Register(descriptor);
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app,
            client,
            jobKinds,
            changes,
            TimeSpan.FromMilliseconds(10));

        await WaitUntilAsync(
            () => service.Snapshot.Value.Jobs.SingleOrDefault()?.Status
                == new AiJobStatusId("vendor-done"),
            TimeSpan.FromSeconds(3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshHandler.RefreshCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(service.Snapshot.Value.Error, Is.Null);
            Assert.That(jobKinds.GetStatus(service.Snapshot.Value.Jobs.Single()).Outcome,
                Is.EqualTo(new AiJobOutcomeId("vendor.complete")));
        }
    }

    [Test]
    public async Task ThrowingStatusResolver_DoesNotAbortMonitorPollingPredicates()
    {
        using var handler = new StubHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        using var httpClient = new HttpClient(handler);
        await using var app = new BeutlApiApplication(httpClient, new ExtensionProvider());
        SetAuthenticatedUser(app, httpClient);
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
        await using var jobKinds = new AiJobKindRegistry();
        await using IAiJobKindRegistration registration = jobKinds.Register(
            new AiJobKindDescriptor(
                new AiJobKindId("vendor.throwing"),
                new ThrowingStatusResolver()));
        using var changes = new Subject<Unit>();
        using var service = new AiJobMonitor(
            app, client, jobKinds, changes, TimeSpan.FromMilliseconds(10));

        await service.RefreshAsync(CancellationToken.None);
        using var polling = service.AcquirePolling();
        await Task.Delay(50);
        Assert.That(client.PageRequests, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task KindRegistry_RequiresExplicitReplacementAndRestoresPreviousRegistration()
    {
        await using var registry = new AiJobKindRegistry();
        AiJobKindDescriptor first = CreateDescriptor("vendor.render", "first");
        AiJobKindDescriptor replacement = CreateDescriptor("VENDOR.RENDER", "replacement");
        await using IAiJobKindRegistration registration = registry.Register(first);

        Assert.Throws<ArgumentException>(() => registry.Register(replacement));
        await using IAiJobKindRegistration replacementRegistration = registry.Register(
            replacement,
            AiJobKindRegistrationMode.Replace);
        Assert.That(registry.TryAcquire(first.Kind, out IAiJobKindLease? replacementLease), Is.True);
        using (replacementLease)
        {
            Assert.That(replacementLease!.Descriptor, Is.SameAs(replacement));
        }

        await replacementRegistration.DisposeAsync();

        Assert.That(registry.TryAcquire(first.Kind, out IAiJobKindLease? firstLease), Is.True);
        using (firstLease)
        {
            Assert.That(firstLease!.Descriptor, Is.SameAs(first));
        }
    }

    [Test]
    public async Task RegistrationDispose_RetiresBeforeWaitingAndDrainsActiveLeases()
    {
        await using var registry = new AiJobKindRegistry();
        AiJobKindDescriptor fallback = CreateDescriptor("vendor.render", "Fallback");
        AiJobKindDescriptor replacement = CreateDescriptor("vendor.render", "Replacement");
        await using IAiJobKindRegistration fallbackRegistration = registry.Register(fallback);
        IAiJobKindRegistration replacementRegistration = registry.Register(
            replacement,
            AiJobKindRegistrationMode.Replace);
        Assert.That(registry.TryAcquire(replacement.Kind, out IAiJobKindLease? activeLease), Is.True);
        IAiJobKindLease lease = activeLease!;

        Task disposeTask = replacementRegistration.DisposeAsync().AsTask();
        try
        {
            await WaitUntilAsync(IsFallbackActive, TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(disposeTask.IsCompleted, Is.False);
                Assert.That(lease.Descriptor, Is.SameAs(replacement));
            }
        }
        finally
        {
            lease.Dispose();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Throws<ObjectDisposedException>(() => _ = lease.Descriptor);

        bool IsFallbackActive()
        {
            if (!registry.TryAcquire(fallback.Kind, out IAiJobKindLease? candidate))
                return false;

            using (candidate)
            {
                return ReferenceEquals(candidate.Descriptor, fallback);
            }
        }
    }

    [Test]
    public async Task PackageExtensionRemoval_DrainsLeasesBeforeExtensionUnload()
    {
        var extensions = new ExtensionProvider();
        await using var registry = new AiJobKindRegistry(extensions);
        AiJobKindDescriptor descriptor = CreateDescriptor("vendor.package", "Package render");
        var extension = new TestAiJobKindExtension(
            descriptor,
            AiJobKindRegistrationMode.Add);
        extensions.AddExtensions(101, [extension]);
        Assert.That(registry.TryAcquire(descriptor.Kind, out IAiJobKindLease? activeLease), Is.True);
        IAiJobKindLease lease = activeLease!;

        Task removalTask = RemoveAndUnloadAsync();
        async Task RemoveAndUnloadAsync()
        {
            ExtensionRemoval removal = extensions.RemoveExtensions(101);
            await removal.DrainAsync();
            foreach (Extension removed in removal.Extensions)
            {
                removed.Unload();
            }
        }
        try
        {
            await WaitUntilAsync(IsContributionRetired, TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(removalTask.IsCompleted, Is.False);
                Assert.That(extension.UnloadCount, Is.Zero);
                Assert.That(lease.Descriptor, Is.SameAs(descriptor));
            }
        }
        finally
        {
            lease.Dispose();
            await removalTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.That(extension.UnloadCount, Is.EqualTo(1));

        bool IsContributionRetired()
        {
            if (!registry.TryAcquire(descriptor.Kind, out IAiJobKindLease? candidate))
                return true;

            candidate.Dispose();
            return false;
        }
    }

    [Test]
    public async Task PackageExtension_AddCollision_DoesNotDisplaceCurrentDescriptor()
    {
        var extensions = new ExtensionProvider();
        await using var registry = new AiJobKindRegistry(extensions);
        AiJobKindDescriptor host = CreateDescriptor("vendor.package", "Host render");
        AiJobKindDescriptor package = CreateDescriptor("vendor.package", "Package render");
        await using IAiJobKindRegistration hostRegistration = registry.Register(host);
        var extension = new TestAiJobKindExtension(package, AiJobKindRegistrationMode.Add);

        extensions.AddExtensions(102, [extension]);

        Assert.That(registry.TryAcquire(host.Kind, out IAiJobKindLease? lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Descriptor, Is.SameAs(host));
        }

        await extensions.RemoveExtensions(102).DrainAsync();
    }

    [Test]
    public async Task PackageExtension_ExplicitReplace_RestoresCurrentDescriptorOnRemoval()
    {
        var extensions = new ExtensionProvider();
        await using var registry = new AiJobKindRegistry(extensions);
        AiJobKindDescriptor host = CreateDescriptor("vendor.package", "Host render");
        AiJobKindDescriptor package = CreateDescriptor("vendor.package", "Package render");
        await using IAiJobKindRegistration hostRegistration = registry.Register(host);
        var extension = new TestAiJobKindExtension(package, AiJobKindRegistrationMode.Replace);

        extensions.AddExtensions(103, [extension]);

        Assert.That(registry.TryAcquire(host.Kind, out IAiJobKindLease? replacementLease), Is.True);
        using (replacementLease)
        {
            Assert.That(replacementLease!.Descriptor, Is.SameAs(package));
        }

        await extensions.RemoveExtensions(103).DrainAsync();

        Assert.That(registry.TryAcquire(host.Kind, out IAiJobKindLease? restoredLease), Is.True);
        using (restoredLease)
        {
            Assert.That(restoredLease!.Descriptor, Is.SameAs(host));
        }
    }

    [Test]
    public async Task PackageExtensions_ComposeReplacementAfterItsBaseInOneChange()
    {
        var extensions = new ExtensionProvider();
        await using var registry = new AiJobKindRegistry(extensions);
        AiJobKindDescriptor baseDescriptor = CreateDescriptor("vendor.fixed-point", "Base");
        AiJobKindDescriptor replacementDescriptor = CreateDescriptor(
            "vendor.fixed-point",
            "Replacement");
        var replacement = new TestAiJobKindExtension(
            replacementDescriptor,
            AiJobKindRegistrationMode.Replace);
        var @base = new TestAiJobKindExtension(
            baseDescriptor,
            AiJobKindRegistrationMode.Add);

        // Deliberately enumerate the replacement before the base it replaces.
        extensions.AddExtensions(104, [replacement, @base]);

        Assert.That(registry.TryAcquire(baseDescriptor.Kind, out IAiJobKindLease? lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Descriptor, Is.SameAs(replacementDescriptor));
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(replacement.DescriptorReadCount, Is.EqualTo(1));
            Assert.That(replacement.RegistrationModeReadCount, Is.EqualTo(1));
            Assert.That(@base.DescriptorReadCount, Is.EqualTo(1));
            Assert.That(@base.RegistrationModeReadCount, Is.EqualTo(1));
        }

        await extensions.RemoveExtensions(104).DrainAsync();
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
    public async Task UnregisteredKind_HasUnknownNonPollingSemanticsAndNoDescriptorLease()
    {
        await using var registry = new AiJobKindRegistry();
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
            Assert.That(registry.TryAcquire(job.Kind, out _), Is.False);
        }
    }

    [Test]
    public async Task UnretainedRegistration_DoesNotRootExtensionOwnedComponents()
    {
        await using var registry = new AiJobKindRegistry();
        WeakReference resolverReference = RegisterEphemeralDescriptor(registry);

        CollectUntilDead(resolverReference);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolverReference.IsAlive, Is.False);
            Assert.That(registry.TryAcquire(new AiJobKindId("ephemeral.kind"), out _), Is.False);
        }
    }

    [Test]
    public async Task AbandonedRegistration_CannotBeReacquiredWhileItsLeaseFinishes()
    {
        await using var registry = new AiJobKindRegistry();
        (WeakReference registrationReference, WeakReference resolverReference, IAiJobKindLease lease)
            = RegisterEphemeralDescriptorWithLease(registry);

        CollectUntilDead(registrationReference);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registrationReference.IsAlive, Is.False);
            Assert.That(resolverReference.IsAlive, Is.True);
            Assert.That(registry.TryAcquire(new AiJobKindId("ephemeral.kind"), out _), Is.False);
        }

        lease.Dispose();
        CollectUntilDead(resolverReference);
        Assert.That(resolverReference.IsAlive, Is.False);
    }

    private static AiJobKindDescriptor CreateDescriptor(string kind, string displayName)
        => new(
            new AiJobKindId(kind),
            new AiJobStatusMap([]));

    private static WeakReference RegisterEphemeralDescriptor(AiJobKindRegistry registry)
    {
        var resolver = new EphemeralStatusResolver();
        var descriptor = new AiJobKindDescriptor(
            new AiJobKindId("ephemeral.kind"),
            resolver);
        _ = registry.Register(descriptor);
        return new WeakReference(resolver);
    }

    private static (WeakReference Registration, WeakReference Resolver, IAiJobKindLease Lease)
        RegisterEphemeralDescriptorWithLease(AiJobKindRegistry registry)
    {
        var resolver = new EphemeralStatusResolver();
        var descriptor = new AiJobKindDescriptor(
            new AiJobKindId("ephemeral.kind"),
            resolver);
        IAiJobKindRegistration registration = registry.Register(descriptor);
        Assert.That(registry.TryAcquire(descriptor.Kind, out IAiJobKindLease? lease), Is.True);
        return (new WeakReference(registration), new WeakReference(resolver), lease!);
    }

    private static void CollectUntilDead(WeakReference reference)
    {
        for (int attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellationTokenSource = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(10, cancellationTokenSource.Token);
        }
    }

    private static void SetAuthenticatedUser(
        BeutlApiApplication app,
        HttpClient httpClient,
        string id = "test-user")
    {
        var profileResponse = new ProfileResponse
        {
            Id = id,
            Name = "test",
            DisplayName = "Test User",
            Bio = null,
            IconId = null,
            IconUrl = null,
        };
        var profile = new Profile(profileResponse, app);
        var authResponse = new AuthResponse
        {
            Token = "token",
            RefreshToken = "refresh-token",
            Expiration = DateTime.UtcNow.AddHours(1),
        };
        var user = new AuthenticatedUser(profile, authResponse, app, DateTime.UtcNow);

        FieldInfo field = typeof(BeutlApiApplication).GetField(
            "_authenticatedUser",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var property = (ReactivePropertySlim<AuthenticatedUser?>)field.GetValue(app)!;
        property.Value = user;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly object _requestsGate = new();
        private readonly List<string> _requests = [];

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
            : this((request, _) => responder(request))
        {
        }

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requestsGate)
                {
                    return _requests.ToArray();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_requestsGate)
            {
                _requests.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
            }

            HttpResponseMessage response = await responder(request, cancellationToken);
            response.RequestMessage = request;
            return response;
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
        private int _refreshCount;

        public int RefreshCount => Volatile.Read(ref _refreshCount);

        public Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _refreshCount);
            client.Complete();
            return Task.CompletedTask;
        }
    }

    private sealed class EphemeralStatusResolver : IAiJobStatusResolver
    {
        public AiJobStatusSemantics Resolve(AiJobStatusId status)
            => AiJobStatusSemantics.Unknown;
    }

    private sealed class ThrowingStatusResolver : IAiJobStatusResolver
    {
        public AiJobStatusSemantics Resolve(AiJobStatusId status)
            => throw new InvalidOperationException("status resolver failure");
    }

    private sealed class TestAiJobKindExtension(
        AiJobKindDescriptor descriptor,
        AiJobKindRegistrationMode registrationMode) : AiJobKindExtension
    {
        public int UnloadCount { get; private set; }

        public int DescriptorReadCount { get; private set; }

        public int RegistrationModeReadCount { get; private set; }

        public override AiJobKindDescriptor Descriptor
        {
            get
            {
                DescriptorReadCount++;
                return descriptor;
            }
        }

        public override AiJobKindRegistrationMode RegistrationMode
        {
            get
            {
                RegistrationModeReadCount++;
                return registrationMode;
            }
        }

        public override void Unload()
        {
            UnloadCount++;
            base.Unload();
        }
    }
}
