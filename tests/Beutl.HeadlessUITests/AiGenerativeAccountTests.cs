using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Sessions;
using Beutl.Api.Services;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Language;
using Beutl.NodeGraph.Generative;
using Beutl.NodeGraph.Nodes.Generative;
using Beutl.ProjectSystem;
using Beutl.Services.AI;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiGenerativeAccountTests
{
    [AvaloniaTest]
    [TestCase("catalog", "account-b")]
    [TestCase("catalog", null)]
    [TestCase("availability", "account-b")]
    [TestCase("availability", null)]
    [TestCase("image submit", "account-b")]
    [TestCase("image submit", null)]
    [TestCase("image refusal", "account-b")]
    [TestCase("video poll", "account-b")]
    [TestCase("video poll", null)]
    [TestCase("video result", "account-b")]
    [TestCase("video result", null)]
    public async Task IdentityChangeStopsBeforeTheNextAuthenticatedCall(string boundary, string? nextAccount)
    {
        using var state = new JobState();
        bool video = boundary.StartsWith("video", StringComparison.Ordinal);
        state.AfterCall = call =>
        {
            if (call == boundary || (call == "image submit" && boundary == "image refusal"))
                state.ChangeAccount(nextAccount);
        };
        state.Images
            .Setup(service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(), It.IsAny<IProgress<AiImagePreview>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiImageGenerationRequest request, IProgress<AiImagePreview>? _, CancellationToken _) =>
            {
                state.SubmittedKey = request.IdempotencyKey;
                state.Record("image submit");
                if (boundary == "image refusal")
                    throw new AiPlanRequiredException();
                return new AiImageResult(null, new AiContentId("file"), JobState.ContentUri);
            });
        state.Videos
            .Setup(service => service.GetAsync(It.IsAny<AiJobId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiJobId id, CancellationToken _) =>
            {
                state.Record("video poll");
                return boundary == "video poll"
                    ? new AiVideoJob(id, AiJobStatuses.Queued, null, null, null)
                    : new AiVideoJob(id, AiJobStatuses.Succeeded, null, JobState.ContentUri, null);
            });
        var progress = new CallbackProgress(value =>
        {
            if (boundary == "video result" && value.Status == NodeGraphStrings.Generative_Loading)
                state.ChangeAccount(nextAccount);
        });

        GenerativeExecutionException? failure = await Assert.ThrowsAsync<GenerativeExecutionException>(() =>
            state.Executor.ExecuteAsync(video ? VideoRequest() : ImageRequest(), progress, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        string[] expectedCalls = boundary switch
        {
            "catalog" => ["catalog"],
            "availability" => ["catalog", "entitlements", "availability"],
            "image submit" or "image refusal" => ["catalog", "entitlements", "availability", "image submit"],
            _ => ["catalog", "entitlements", "availability", "video submit", "video poll"],
        };
        Assert.Multiple(() =>
        {
            Assert.That(failure?.InnerException, Is.TypeOf<AuthenticationRequiredException>());
            Assert.That(failure?.SettledRequest, Is.False);
            Assert.That(state.Calls, Is.EqualTo(expectedCalls));
            state.Content.VerifyNoOtherCalls();
            state.Library.VerifyNoOtherCalls();
            if (state.SubmittedKey is { } key)
            {
                string operation = video ? "video.generate" : "image.generate";
                Assert.That(state.Store.PendingFor("account-a", operation).Single().Key, Is.EqualTo(key));
                Assert.That(state.Store.PendingFor("account-b", operation), Is.Empty);
            }
        });
    }

    [AvaloniaTest]
    public async Task AKeyedGenerationRetryAfterReopeningCannotBuyAChangedDefault()
    {
        (_, Scene scene, _) = await TestShell.OpenNewSceneAsync("agent-ai-default-retry");
        using var state = new JobState();
        var submitted = new List<AiImageGenerationRequest>();
        state.Images.Setup(service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(), It.IsAny<IProgress<AiImagePreview>?>(), It.IsAny<CancellationToken>()))
            .Returns((AiImageGenerationRequest request, IProgress<AiImagePreview>? _, CancellationToken _) =>
            {
                submitted.Add(request);
                return Task.FromException<AiImageResult>(new HttpRequestException("The request did not reach the service."));
            });
        using var originalJobs = new AgentAiJobManager();
        var originalTools = new AgentHostAiTools(TestShell.Editor, originalJobs, state.Backend, new AgentSessionManager());
        var original = await originalTools.GenerateImage("a cat", waitSeconds: 10, requestKey: "paid");
        Assert.That(original.Value?.Status, Is.EqualTo(AgentAiJobStatus.Failed), original.Error?.Message);
        Assert.That(submitted.Single().AspectRatio.Value, Is.EqualTo("4:3"));

        scene.FrameSize.Value = new Beutl.Media.PixelSize(1920, 1080);
        using var retryJobs = new AgentAiJobManager();
        var retryTools = new AgentHostAiTools(TestShell.Editor, retryJobs, state.Backend, new AgentSessionManager());
        var changed = await retryTools.GenerateImage("a cat", waitSeconds: 10, requestKey: "paid");

        Assert.Multiple(() =>
        {
            Assert.That(changed.Value?.ErrorCode, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(changed.Value?.ErrorMessage, Does.Contain("different resolved settings"));
            Assert.That(submitted, Has.Count.EqualTo(1));
        });

        scene.FrameSize.Value = new Beutl.Media.PixelSize(640, 480);
        using var reopenedJobs = new AgentAiJobManager();
        var reopenedTools = new AgentHostAiTools(TestShell.Editor, reopenedJobs, state.Backend, new AgentSessionManager());
        await reopenedTools.GenerateImage("a cat", waitSeconds: 10, requestKey: "paid");
        Assert.That(submitted.Select(request => request.IdempotencyKey),
            Is.EqualTo(new[] { submitted[0].IdempotencyKey, submitted[0].IdempotencyKey }));
    }

    [AvaloniaTest]
    public async Task APaidGenerationRetryRecoversWithItsServiceKeyWhenNewWorkIsUnavailable()
    {
        await TestShell.OpenNewSceneAsync("agent-ai-paid-retry");
        using var state = new JobState(TimeSpan.Zero);
        state.Images.Setup(service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(), It.IsAny<IProgress<AiImagePreview>?>(), It.IsAny<CancellationToken>()))
            .Returns((AiImageGenerationRequest request, IProgress<AiImagePreview>? _, CancellationToken _) =>
            {
                state.SubmittedKey = request.IdempotencyKey;
                return Task.FromException<AiImageResult>(new AiResultUnavailableException());
            });
        using var jobs = new AgentAiJobManager();
        var tools = new AgentHostAiTools(TestShell.Editor, jobs, state.Backend, new AgentSessionManager());
        var failed = await tools.GenerateImage("a cat", waitSeconds: 10, requestKey: "paid");
        Assert.That(failed.Value?.Status, Is.EqualTo(AgentAiJobStatus.Failed), failed.Error?.Message);
        Assert.That(state.SubmittedKey, Is.Not.Null);

        state.RefuseNewWork();
        state.Calls.Clear();
        var paidJob = new AiJob(new AiJobId("paid-image"), AiJobKinds.Image, AiJobStatuses.Succeeded,
            System.Text.Json.JsonSerializer.SerializeToElement(new { seed = 5, prompt = "a cat" }), new AiContentId("file"),
            JobState.ContentUri, null, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        {
            Model = new AiModelId("original-model"),
        };
        state.Jobs.SetupSequence(service => service.GetByIdempotencyKeyAsync(state.SubmittedKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(paidJob with { Status = AiJobStatuses.Running, ContentUri = null })
            .ThrowsAsync(new HttpRequestException("Temporary service failure."))
            .ReturnsAsync(paidJob)
            .ReturnsAsync(paidJob);
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        state.Content.Setup(service => service.CopyToAsync(JobState.ContentUri, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (Uri _, Stream destination, CancellationToken ct) =>
            {
                await destination.WriteAsync(png, ct);
                return new AiContentDownload(null);
            });
        state.Library.Setup(library => library.Record(GenerativeOperation.ImageGeneration, "a cat"));

        var recovered = await tools.GenerateImage("a cat", waitSeconds: 10, requestKey: "paid");
        Assert.That(recovered.Value?.Status, Is.EqualTo(AgentAiJobStatus.Succeeded), recovered.Value?.ErrorMessage ?? recovered.Error?.Message);
        Assert.That(state.Store.PendingFor("account-a", "image.generate"), Is.Empty);
        state.Library.Verify(library => library.Record(GenerativeOperation.ImageGeneration, "a cat"), Times.Once);

        using var reopenedJobs = new AgentAiJobManager();
        var reopenedTools = new AgentHostAiTools(TestShell.Editor, reopenedJobs, state.Backend, new AgentSessionManager());
        var reopened = await reopenedTools.GenerateImage("a cat", waitSeconds: 10, requestKey: "paid");
        Assert.Multiple(() =>
        {
            Assert.That(recovered.Value?.JobId, Is.Not.EqualTo(failed.Value?.JobId));
            Assert.That(reopened.Value?.Status, Is.EqualTo(AgentAiJobStatus.Succeeded), reopened.Value?.ErrorMessage ?? reopened.Error?.Message);
            Assert.That(reopened.Value?.Output?.ModelId, Is.EqualTo("original-model"));
            Assert.That(reopened.Value?.Output?.Seed, Is.EqualTo(5));
            Assert.That(state.Calls, Is.Empty, "collecting paid results does not consult the catalog or check the balance");
            state.Jobs.Verify(service => service.GetByIdempotencyKeyAsync(state.SubmittedKey!, It.IsAny<CancellationToken>()), Times.Exactly(4));
            state.Images.Verify(service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(), It.IsAny<IProgress<AiImagePreview>?>(), It.IsAny<CancellationToken>()), Times.Once);
        });
    }

    [AvaloniaTest]
    public async Task SwitchingAccountsCancelsTheInFlightPollEvenAfterSwitchingBack()
    {
        using var state = new JobState();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Videos.Setup(video => video.GetAsync(It.IsAny<AiJobId>(), It.IsAny<CancellationToken>()))
            .Returns(async (AiJobId _, CancellationToken token) =>
            {
                state.Record("video poll");
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new AssertionException("The account change should have cancelled this poll.");
            });
        Task<GenerativeExecutionResult> job = state.Executor.ExecuteAsync(
            VideoRequest(), new CallbackProgress(_ => { }), CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        state.ChangeAccount("account-b");
        state.ChangeAccount("account-a");

        GenerativeExecutionException? failure = await Assert.ThrowsAsync<GenerativeExecutionException>(
            () => job.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Multiple(() =>
        {
            Assert.That(failure?.InnerException, Is.TypeOf<AuthenticationRequiredException>());
            Assert.That(state.Calls, Is.EqualTo(new[] { "catalog", "entitlements", "availability", "video submit", "video poll" }));
            Assert.That(state.Store.PendingFor("account-a", "video.generate").Single().Key, Is.EqualTo(state.SubmittedKey));
        });
    }

    [AvaloniaTest]
    public async Task AuthenticationFailureInAPollIsNotRetried()
    {
        using var state = new JobState();
        state.Videos.Setup(video => video.GetAsync(It.IsAny<AiJobId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationRequiredException(new TimeoutException()));

        GenerativeExecutionException? failure = await Assert.ThrowsAsync<GenerativeExecutionException>(() =>
            state.Executor.ExecuteAsync(VideoRequest(), new CallbackProgress(_ => { }), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.That(failure?.InnerException, Is.TypeOf<AuthenticationRequiredException>());
        state.Videos.Verify(video => video.GetAsync(It.IsAny<AiJobId>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AiImageGenerationNodeRequest ImageRequest() => new(new AiImageGenerationNode())
    {
        Prompt = "a cat",
        AspectRatio = "1:1",
        RequestKeySeed = Guid.NewGuid().ToString("N"),
        ParameterFingerprint = "picture",
    };

    private static AiVideoGenerationNodeRequest VideoRequest() => new(new AiVideoGenerationNode())
    {
        Prompt = "a cat",
        DurationSeconds = 4,
        Resolution = "720p",
        AspectRatio = "16:9",
        RequestKeySeed = Guid.NewGuid().ToString("N"),
        ParameterFingerprint = "clip",
    };

    private sealed class CallbackProgress(Action<GenerativeProgress> report) : IProgress<GenerativeProgress>
    {
        public void Report(GenerativeProgress value) => report(value);
    }

    private sealed class JobState : IDisposable
    {
        internal static readonly Uri ContentUri = new("https://beutl.test/content/result");
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"beutl-ai-job-{Guid.NewGuid():N}");
        private readonly ReactivePropertySlim<AiAuthenticatedRequestIdentity?> _identity = new(new("account-a", User: null));
        private readonly ReactivePropertySlim<AiEntitlements?> _entitlements = new();
        private readonly AiRequestRecoveryContext _recovery;

        public JobState(TimeSpan? pollInterval = null)
        {
            Store = new FileAiRequestKeyMap(_directory);
            _recovery = new AiRequestRecoveryContext(Store, () => _identity.Value, _identity);
            var catalog = new Mock<IAiModelCatalogService>(MockBehavior.Strict);
            catalog.Setup(service => service.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            {
                Record("catalog");
                return AiModelCatalog.Empty;
            });
            var entitlementService = new Mock<IAiEntitlementService>(MockBehavior.Strict);
            entitlementService.SetupGet(service => service.Entitlements).Returns(_entitlements);
            entitlementService.Setup(service => service.RefreshAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() =>
            {
                Record("entitlements");
                return null;
            });
            var availability = new Mock<IAiOperationAvailabilityService>(MockBehavior.Strict);
            availability.Setup(service => service.CheckAsync(It.IsAny<AiOperationAvailabilityRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Record("availability");
                    return true;
                });
            Videos.Setup(service => service.CreateAsync(It.IsAny<AiVideoGenerationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AiVideoGenerationRequest request, CancellationToken _) =>
                {
                    SubmittedKey = request.IdempotencyKey;
                    Record("video submit");
                    return new AiVideoGenerationResult(new AiJobId("video"), AiJobStatuses.Queued);
                });
            var scene = new Scene(640, 480, "nodes") { Uri = new Uri(Path.Combine(_directory, "scene.scene")) };
            var models = new AiGenerativeModelCatalog(catalog.Object, entitlementService.Object);
            Jobs.Setup(service => service.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AiJobNotFoundException());
            Executor = new AiGenerativeNodeExecutor(
                scene, models, Images.Object,
                availability.Object, Content.Object, Library.Object, Mock.Of<IAiImageEditingService>(), Videos.Object,
                AiJobKindRegistry.CreateBuiltIn(Videos.Object), TestShell.Decoders, _recovery, Jobs.Object)
            {
                PollInterval = pollInterval ?? TimeSpan.FromHours(1),
            };
            Backend = new AgentHostAiBackend(_recovery, entitlementService.Object, availability.Object,
                Mock.Of<IAiTranscriptionService>(), () => models, _ => Executor, catalog.Object, TestShell.Decoders);
        }

        public FileAiRequestKeyMap Store { get; }
        public AiGenerativeNodeExecutor Executor { get; }
        public AgentHostAiBackend Backend { get; }
        public Mock<IAiImageGenerationService> Images { get; } = new(MockBehavior.Strict);
        public Mock<IAiVideoService> Videos { get; } = new(MockBehavior.Strict);
        public Mock<IAiJobClient> Jobs { get; } = new(MockBehavior.Strict);
        public Mock<IAuthenticatedContentService> Content { get; } = new(MockBehavior.Strict);
        public Mock<IGenerativePromptLibrary> Library { get; } = new(MockBehavior.Strict);
        public List<string> Calls { get; } = [];
        public string? SubmittedKey { get; set; }
        public Action<string>? AfterCall { get; set; }

        public void ChangeAccount(string? id) => _identity.Value = id is null ? null : new(id, User: null);

        public void RefuseNewWork() => _entitlements.Value = new AiEntitlements(
            null, null, null, null, false, false, new AiBalance(new AiMonthlyUsage(0, 0, false), 0, false),
            new AiOperationAvailability([]));

        public void Record(string call)
        {
            Assert.That(AiAuthenticatedRequestScope.Identity?.AccountId, Is.EqualTo("account-a"));
            Calls.Add(call);
            AfterCall?.Invoke(call);
        }

        public void Dispose()
        {
            _recovery.Dispose();
            _identity.Dispose();
            _entitlements.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
