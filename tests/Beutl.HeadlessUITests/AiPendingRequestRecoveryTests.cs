using System.Net;
using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.NUnit;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using static Beutl.HeadlessUITests.AiTestSupport;

namespace Beutl.HeadlessUITests;

// A request whose answer was lost is collected by looking its job up by key, not by sending it again.
[TestFixture]
public sealed class AiPendingRequestRecoveryTests
{
    private static readonly byte[] s_png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Beutl.HeadlessUITests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [AvaloniaTest]
    public async Task AnImageWhoseAnswerWasLostIsCollectedThroughTheLookupAfterARestart()
    {
        var requests = new List<(string Path, string? Key)>();
        using var handler = new StubHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (RecoveryCatalog(path) is { } catalog)
                return catalog;
            switch (path)
            {
                case "/api/v3/ai/images":
                    requests.Add((path, IdempotencyKeyOf(request)));
                    throw new HttpRequestException("The connection was reset.");
                case "/api/v3/ai/jobs/by-idempotency-key":
                    requests.Add((path, IdempotencyKeyOf(request)));
                    return JsonResponse(HttpStatusCode.OK, JobJson("image", "succeeded", "https://beutl.beditor.net/api/contents/lost-file"));
                case "/api/contents/lost-file":
                    return ByteResponse(s_png, "image/png");
                default:
                    return NotFoundResponse();
            }
        });
        await using BeutlApiApplication app = CreateApp(handler);
        await using (var first = CreateImageGenerationDialog(app, context: NewContext("test-user"), planCoordinator: new TestPlanCoordinator()))
        {
            await WaitUntilAsync(() => first.Usage.HasSnapshot.Value && first.ModelPicker.IsLoaded.Value);
            first.Prompt.Value = "a lighthouse";
            await WaitUntilAsync(() => first.CanGenerate.Value);
            await first.Generate.ExecuteAsync();
            Assert.That(first.RecoveryAvailable.Value, Is.True, "the lost request is listed for recovery");
        }

        await using var restarted = CreateImageGenerationDialog(app, context: NewContext("test-user"), planCoordinator: new TestPlanCoordinator());
        await WaitUntilAsync(() => restarted.ResultImage.Value is not null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests.Select(request => request.Path), Is.EqualTo(new[] { "/api/v3/ai/images", "/api/v3/ai/jobs/by-idempotency-key" }));
            Assert.That(requests[1].Key, Is.EqualTo(requests[0].Key));
            Assert.That(restarted.RecoveryAvailable.Value, Is.False);
            Assert.That(new FileAiRequestKeyMap(_root).PendingFor("test-user", "image.generate"), Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task ARunningJobStaysListedAndAJobNeverMadeLetsTheRowGo()
    {
        var map = new FileAiRequestKeyMap(_root);
        map.GetOrAdd("test-user", "image.edit.upscale", "pending", null, () => "pending-key");
        bool made = true;
        using var handler = new StubHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (RecoveryCatalog(path) is { } catalog)
                return catalog;
            return path == "/api/v3/ai/jobs/by-idempotency-key"
                ? made
                    ? JsonResponse(HttpStatusCode.OK, JobJson("image", "running"))
                    : ErrorResponse(HttpStatusCode.NotFound, "aiJobNotFound", "No job.")
                : NotFoundResponse();
        }, handleAvailability: false);
        await using BeutlApiApplication app = CreateApp(handler);
        await using var edit = CreateImageEditDialog(app, context: NewContext("test-user"), planCoordinator: new TestPlanCoordinator());

        await WaitUntilAsync(() => edit.Error.Value is not null && !edit.IsEditing.Value);
        string? running = edit.Error.Value;
        bool stillListed = edit.RecoveryAvailable.Value;
        made = false;
        edit.SelectedRecoveryAttempt.Value = edit.RecoveryAttempts.Value.Single();
        edit.RecoverSelectedAttempt.Execute();
        await WaitUntilAsync(() => edit.Error.Value != running && !edit.IsEditing.Value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(running, Is.EqualTo(Strings.AiRequestInProgress));
            Assert.That(stillListed, Is.True);
            Assert.That(edit.Error.Value, Is.EqualTo(Strings.AiPendingRequestNotReceived));
            Assert.That(edit.RecoveryAvailable.Value, Is.False);
            Assert.That(map.PendingFor("test-user", "image.edit"), Is.Empty);
        }
    }

    [AvaloniaTest]
    [TestCase(409, "aiRequestWasDeleted")]
    [TestCase(200, "failed")]
    public async Task ADeletedOrFailedJobLetsTheRowGo(int status, string outcome)
    {
        var map = new FileAiRequestKeyMap(_root);
        map.GetOrAdd("test-user", "image.generate", "pending", null, () => "pending-key");
        using var handler = new StubHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (RecoveryCatalog(path) is { } catalog)
                return catalog;
            return path != "/api/v3/ai/jobs/by-idempotency-key"
                ? NotFoundResponse()
                : status == 200
                    ? JsonResponse(HttpStatusCode.OK, JobJson("image", "failed", error: "aiProviderError"))
                    : ErrorResponse((HttpStatusCode)status, outcome, "Gone.");
        }, handleAvailability: false);
        await using BeutlApiApplication app = CreateApp(handler);
        await using var generation = CreateImageGenerationDialog(app, context: NewContext("test-user"), planCoordinator: new TestPlanCoordinator());

        await WaitUntilAsync(() => !generation.RecoveryAvailable.Value && generation.Error.Value is not null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(generation.Error.Value, Is.EqualTo(status == 200 ? Strings.AiProviderError : Strings.AiRequestWasDeleted));
            Assert.That(map.PendingFor("test-user", "image.generate"), Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task AVideoStillRunningIsWaitedForAndCollected()
    {
        var map = new FileAiRequestKeyMap(_root);
        map.GetOrAdd("test-user", "video.generate", "pending", null, () => "pending-key");
        using var handler = new StubHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (RecoveryCatalog(path) is { } catalog)
                return catalog;
            return path switch
            {
                "/api/v3/ai/jobs/by-idempotency-key" => JsonResponse(HttpStatusCode.OK, JobJson("video", "running", id: "lost-video")),
                "/api/v3/ai/videos/lost-video" => JsonResponse(HttpStatusCode.OK, """
                    { "jobId": "lost-video", "status": "succeeded", "fileId": "lost-video-file",
                      "url": "https://beutl.beditor.net/api/contents/lost-video-file", "error": null }
                    """),
                "/api/contents/lost-video-file" => ByteResponse([1, 2, 3, 4], "video/mp4"),
                _ => NotFoundResponse(),
            };
        }, handleAvailability: false);
        await using BeutlApiApplication app = CreateApp(handler);
        await using var video = CreateVideoGenerationDialog(app, context: NewContext("test-user"), planCoordinator: new TestPlanCoordinator());
        video.VideoDurationReader = _ => TimeSpan.FromSeconds(4);

        await WaitUntilAsync(() => video.ResultVideoPath.Value is not null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(video.RecoveryAvailable.Value, Is.False);
            Assert.That(map.PendingFor("test-user", "video.generate"), Is.Empty);
        }
    }

    [AvaloniaTest]
    public async Task AbandoningAnAttemptLetsItsRowGoWithoutAskingTheServer()
    {
        var map = new FileAiRequestKeyMap(_root);
        map.GetOrAdd("test-user", "image.generate", "first", null, () => "first-key");
        map.GetOrAdd("test-user", "image.generate", "second", null, () => "second-key");
        int lookups = 0;
        using var handler = new StubHandler(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/api/v3/ai/jobs/by-idempotency-key")
                Interlocked.Increment(ref lookups);
            return RecoveryCatalog(path) ?? NotFoundResponse();
        }, handleAvailability: false);
        await using BeutlApiApplication app = CreateApp(handler);
        await using var generation = CreateImageGenerationDialog(app, context: NewContext("test-user"), planCoordinator: new TestPlanCoordinator());
        await WaitUntilAsync(() => generation.RecoveryAttempts.Value.Count == 2);

        generation.SelectedRecoveryAttempt.Value = generation.RecoveryAttempts.Value.Single(attempt => attempt.Key == "first-key");
        generation.AbandonSelectedAttempt.Execute();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lookups, Is.Zero, "two attempts wait for the person to choose");
            Assert.That(generation.RecoveryAttempts.Value.Select(attempt => attempt.Key), Is.EqualTo(new[] { "second-key" }));
            Assert.That(map.PendingFor("test-user", "image.generate").Select(row => row.Key), Is.EqualTo(new[] { "second-key" }));
        }
    }

    [AvaloniaTest]
    public async Task AccountSwitchRejectsLateNonCooperativeImageResult()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "Beutl.HeadlessUITests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? account = "account-a";
        var store = new FileAiRequestKeyMap(root);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = CreateLateAccountSwitchHandler(started, release, s_png);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://beutl.beditor.net"),
        };
        await using var app = new BeutlApiApplication(client, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var context = new AiRequestRecoveryContext(
            store,
            () => account is { } value
                ? new AiAuthenticatedRequestIdentity(value, User: null)
                : null);
        await using var generation = CreateImageGenerationDialog(app, context: context, planCoordinator: new TestPlanCoordinator());
        await WaitUntilAsync(() => generation.Usage.HasSnapshot.Value
            && generation.ModelPicker.IsLoaded.Value);
        generation.Prompt.Value = "Account A secret";
        await WaitUntilAsync(() => generation.CanGenerate.Value);

        Task operation = generation.Generate.ExecuteAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        account = "account-b";
        context.RefreshIdentity();
        generation.Prompt.Value = "Account B prompt";

        Assert.Multiple(() =>
        {
            Assert.That(generation.IsGenerating.Value, Is.False);
            Assert.That(generation.ResultImage.Value, Is.Null);
            Assert.That(generation.PreviewImage.Value, Is.Null);
            Assert.That(generation.Error.Value, Is.Null);
        });

        release.TrySetResult();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(generation.Prompt.Value, Is.EqualTo("Account B prompt"));
            Assert.That(generation.IsGenerating.Value, Is.False);
            Assert.That(generation.ResultImage.Value, Is.Null);
            Assert.That(generation.PreviewImage.Value, Is.Null);
            Assert.That(generation.Error.Value, Is.Null);
            Assert.That(store.PendingFor("account-a", "image.generate"), Has.Count.EqualTo(1));
            Assert.That(store.PendingFor("account-b", "image.generate"), Is.Empty);
        });

        Directory.Delete(root, recursive: true);
    }

    [AvaloniaTest]
    public async Task AccountSwitchClosesPublicationBeforeSynchronousCancellationCallbacks()
    {
        string root = Path.Combine(Path.GetTempPath(), "Beutl.HeadlessUITests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string? account = "account-a";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = CreateLateAccountSwitchHandler(started, release, s_png);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://beutl.beditor.net") };
        await using var app = new BeutlApiApplication(client, new ExtensionProvider());
        SetAuthenticatedUser(app);
        using var context = new AiRequestRecoveryContext(
            new FileAiRequestKeyMap(root),
            () => account is { } value
                ? new AiAuthenticatedRequestIdentity(value, User: null)
                : null);
        await using var generation = CreateImageGenerationDialog(app, context: context, planCoordinator: new TestPlanCoordinator());
        await WaitUntilAsync(() => generation.Usage.HasSnapshot.Value && generation.ModelPicker.IsLoaded.Value);
        generation.Prompt.Value = "old account prompt";
        await WaitUntilAsync(() => generation.CanGenerate.Value);

        Task request = generation.Generate.ExecuteAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            FieldInfo field = typeof(AiImageGenerationDialogViewModel).GetField(
                "_runningRequest", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var running = (IdentityOperationLifetime.Operation)field.GetValue(generation)!;
            var publication = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = running.CancellationToken.Register(() =>
                publication.TrySetResult(running.TryPublish(() => generation.Prompt.Value = "stale callback")));

            account = "account-b";
            context.RefreshIdentity();

            // Switching closes publication synchronously, but cancellation callbacks run on a worker.
            bool published = await publication.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(published, Is.False);
                Assert.That(generation.Prompt.Value, Is.Empty);
                Assert.That(generation.ResultImage.Value, Is.Null);
            });
        }
        finally
        {
            release.TrySetResult();
            await request.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, recursive: true);
        }
    }

    // Answers the entitlement and capability reads of the recovery tests, or null for another path.
    private static HttpResponseMessage? RecoveryCatalog(string path) => path switch
    {
        "/api/v3/user/entitlements" => JsonResponse(
            HttpStatusCode.OK,
            EntitlementsJson(["image.generate", "image.edit.upscale", "video.generate"], additionalCredits: 10)),
        "/api/v3/ai/capabilities" => JsonResponse(HttpStatusCode.OK, RecoveryCapabilitiesJson()),
        _ => null,
    };

    private static HttpResponseMessage NotFoundResponse()
        => new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };

    private AiRequestRecoveryContext NewContext(string account)
        => new(new FileAiRequestKeyMap(_root), () => new AiAuthenticatedRequestIdentity(account, User: null));

    private static BeutlApiApplication CreateApp(StubHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://beutl.beditor.net") };
        var app = new BeutlApiApplication(client, new ExtensionProvider());
        SetAuthenticatedUser(app);
        return app;
    }

    private static string JobJson(string kind, string status, string? url = null, string? error = null, string id = "job-1")
        => JsonSerializer.Serialize(new
        {
            id,
            kind,
            status,
            fileId = url is null ? null : "file",
            url,
            error,
            canRetry = false,
            createdAt = "2026-10-20T00:00:00Z",
            updatedAt = "2026-10-20T00:00:00Z",
        });

    private static Task WaitUntilAsync(Func<bool> condition)
        => HeadlessTestHelpers.WaitUntilAsync(condition, TimeSpan.FromSeconds(8), settleAfterDelay: true);

    private static string RecoveryCapabilitiesJson() => """
        {
          "operations": {
            "image.generate": {
              "models": [{ "id": "current-image", "displayName": "Current image", "costTier": "low", "isDefault": true, "aspectRatios": ["1:1"], "backgrounds": ["auto"], "seed": false, "maxReferenceImages": 0 }],
              "aspectRatios": ["1:1"], "backgrounds": ["auto"]
            },
            "image.edit.upscale": {
              "models": [{ "id": "current-edit", "displayName": "Current edit", "costTier": "low", "isDefault": true }]
            },
            "video.generate": {
              "models": [{ "id": "current-video", "displayName": "Current video", "costTier": "low", "isDefault": true, "durationsSeconds": [4], "resolutions": ["720p"], "aspectRatios": ["16:9"], "audio": false, "seed": false, "firstFrame": false, "lastFrame": false }],
              "durationsSeconds": [4], "resolutions": ["720p"], "aspectRatios": ["16:9"]
            }
          }
        }
        """;

    // Holds the image request until release completes, then answers it with content.
    private static StubHandler CreateLateAccountSwitchHandler(
        TaskCompletionSource started,
        TaskCompletionSource release,
        byte[] content)
        => new(async (request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (RecoveryCatalog(path) is { } catalog)
                return catalog;
            if (path == "/api/v3/ai/images")
            {
                started.TrySetResult();
                await release.Task;
                return JsonResponse(
                    HttpStatusCode.OK,
                    "{\"jobId\":\"late-account-job\",\"fileId\":\"late-account-file\","
                    + "\"url\":\"https://beutl.beditor.net/api/contents/late-account-file\"}");
            }
            if (path == "/api/contents/late-account-file")
                return ByteResponse(content, "image/png");
            return NotFoundResponse();
        });

    private sealed class TestPlanCoordinator : IAiPlanCoordinator
    {
        public event EventHandler? Refreshed
        {
            add { }
            remove { }
        }

        public void OpenAccountSettings() { }

        public void OpenAiPlan() { }

        public Task<bool> RefreshIfPendingAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);
    }
}
