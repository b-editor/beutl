using Avalonia.Headless.NUnit;
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

        public JobState()
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
            Executor = new AiGenerativeNodeExecutor(
                scene, new AiGenerativeModelCatalog(catalog.Object, entitlementService.Object), Images.Object,
                availability.Object, Content.Object, Library.Object, Mock.Of<IAiImageEditingService>(), Videos.Object,
                AiJobKindRegistry.CreateBuiltIn(Videos.Object), TestShell.Decoders, _recovery)
            {
                PollInterval = TimeSpan.FromHours(1),
            };
        }

        public FileAiRequestKeyMap Store { get; }
        public AiGenerativeNodeExecutor Executor { get; }
        public Mock<IAiImageGenerationService> Images { get; } = new(MockBehavior.Strict);
        public Mock<IAiVideoService> Videos { get; } = new(MockBehavior.Strict);
        public Mock<IAuthenticatedContentService> Content { get; } = new(MockBehavior.Strict);
        public Mock<IGenerativePromptLibrary> Library { get; } = new(MockBehavior.Strict);
        public List<string> Calls { get; } = [];
        public string? SubmittedKey { get; set; }
        public Action<string>? AfterCall { get; set; }

        public void ChangeAccount(string? id) => _identity.Value = id is null ? null : new(id, User: null);

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
