using System.Collections.Concurrent;
using System.Text.Json;
using Beutl.Api.Services;
using Beutl.Language;
using Moq;
using NUnit.Framework;

namespace Beutl.UnitTests.Api;

/// <summary>
/// A retry has to reproduce the job it repeats. The server records the shape it
/// was asked for, and these cover the two ways that can go wrong: reading a key
/// that is no longer written, and repeating a request whose upload was never
/// retained.
/// </summary>
public class AiJobRetryHandlerTests
{
    [Test]
    public async Task ImageRetry_ReusesIdempotencyKeyAcrossAmbiguousResponse()
    {
        var images = new Mock<IAiImageGenerationService>();
        var keys = new List<string?>();
        images.Setup(s => s.GenerateAsync(It.IsAny<AiImageGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiImageGenerationRequest, CancellationToken>((request, _) => keys.Add(request.IdempotencyKey))
            .ThrowsAsync(new IOException("response lost"));
        var handler = new AiImageJobRetryHandler(images.Object, EntitlementService(), AvailabilityService(true), ModelCatalogService(), RetryContext());
        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");

        Assert.ThrowsAsync<IOException>(() => RunRetryAsync(handler, job));
        Assert.ThrowsAsync<IOException>(() => RunRetryAsync(handler, job));

        Assert.That(keys, Has.Count.EqualTo(2));
        Assert.That(keys[1], Is.EqualTo(keys[0]));
    }

    [Test]
    public async Task JobLimitRefusalRetiresTheUnreservedKeyBeforeRetry()
    {
        var images = new Mock<IAiImageGenerationService>();
        var keys = new List<string?>();
        images
            .Setup(service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<AiImageGenerationRequest, CancellationToken>((request, _) =>
            {
                keys.Add(request.IdempotencyKey);
                return keys.Count == 1
                    ? Task.FromException<AiImageResult>(new AiJobLimitReachedException())
                    : Task.FromResult<AiImageResult>(null!);
            });
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService(),
            RetryContext());
        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");

        Assert.ThrowsAsync<AiJobLimitReachedException>(() => RunRetryAsync(handler, job));
        await RunRetryAsync(handler, job);

        Assert.That(keys, Has.Count.EqualTo(2));
        Assert.That(keys[1], Is.Not.EqualTo(keys[0]));
    }

    [Test]
    public void AuthenticationSessionChangeRetainsTheIssuedRetryKey()
    {
        var images = new Mock<IAiImageGenerationService>();
        var keys = new List<string?>();
        images.Setup(service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiImageGenerationRequest, CancellationToken>((request, _) =>
                keys.Add(request.IdempotencyKey))
            .ThrowsAsync(new AuthenticationRequiredException());
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(true),
            ModelCatalogService(),
            RetryContext());
        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");

        Assert.ThrowsAsync<AuthenticationRequiredException>(() => RunRetryAsync(handler, job));
        Assert.ThrowsAsync<AuthenticationRequiredException>(() => RunRetryAsync(handler, job));

        Assert.That(keys, Has.Count.EqualTo(2));
        Assert.That(keys[1], Is.EqualTo(keys[0]));
    }

    [Test]
    public async Task ConcurrentRetriesForOneJobUseOneKey()
    {
        var images = new Mock<IAiImageGenerationService>();
        var keys = new ConcurrentBag<string?>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        images.Setup(s => s.GenerateAsync(It.IsAny<AiImageGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiImageGenerationRequest, CancellationToken>((request, _) => keys.Add(request.IdempotencyKey))
            .Returns(async () =>
            {
                entered.TrySetResult();
                await release.Task;
                return null!;
            });
        var handler = new AiImageJobRetryHandler(images.Object, EntitlementService(), AvailabilityService(true), ModelCatalogService(), RetryContext());
        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");

        AiJobRetryPreparationResult firstResult = await handler.PrepareAsync(job, CancellationToken.None);
        AiJobRetryPreparationResult secondResult = await handler.PrepareAsync(job, CancellationToken.None);
        IAiJobRetryPreparation firstPreparation = firstResult.TakePreparation();
        IAiJobRetryPreparation secondPreparation = secondResult.TakePreparation();
        Task first = firstPreparation.ExecuteAsync(CancellationToken.None);
        await entered.Task;
        Task second = secondPreparation.ExecuteAsync(CancellationToken.None);
        release.TrySetResult();
        await Task.WhenAll(first, second);
        await firstPreparation.DisposeAsync();
        await secondPreparation.DisposeAsync();
        await firstResult.DisposeAsync();
        await secondResult.DisposeAsync();

        Assert.That(keys, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PreparedRetryRejectsSecondExecuteThroughPublicContract()
    {
        var images = new Mock<IAiImageGenerationService>();
        images.Setup(s => s.GenerateAsync(It.IsAny<AiImageGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null!);
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(true),
            ModelCatalogService(),
            RetryContext());

        AiJobRetryPreparationResult result = await handler.PrepareAsync(
            Job("image", "{\"prompt\":\"once\"}"),
            CancellationToken.None);
        IAiJobRetryPreparation preparation = result.TakePreparation();
        await preparation.ExecuteAsync(CancellationToken.None);

        Assert.ThrowsAsync<AiJobRetryPreparationRejectedException>(() =>
            preparation.ExecuteAsync(CancellationToken.None));
        await preparation.DisposeAsync();
        await result.DisposeAsync();
    }

    [Test]
    public async Task RetryAfterASettledAttemptStartsANewGeneration()
    {
        var images = new Mock<IAiImageGenerationService>();
        var keys = new List<string?>();
        images.Setup(s => s.GenerateAsync(It.IsAny<AiImageGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiImageGenerationRequest, CancellationToken>((request, _) => keys.Add(request.IdempotencyKey))
            .ReturnsAsync(() => null!);
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(true),
            ModelCatalogService(),
            RetryContext());
        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");

        await RunRetryAsync(handler, job);
        await RunRetryAsync(handler, job);

        Assert.That(keys, Has.Count.EqualTo(2));
        Assert.That(keys[1], Is.Not.EqualTo(keys[0]));
    }

    [Test]
    public void RetryWithoutAnAuthenticatedAccountDoesNotIssueAKey()
    {
        var images = new Mock<IAiImageGenerationService>();
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(true),
            ModelCatalogService(),
            RetryContext(accountId: null));

        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");
        Assert.ThrowsAsync<AuthenticationRequiredException>(async () =>
        {
            AiJobRetryPreparationResult prepared = await handler.PrepareAsync(job, CancellationToken.None);
            await using (prepared)
            {
                IAiJobRetryPreparation preparation = prepared.TakePreparation();
                await using (preparation)
                {
                    await preparation.ExecuteAsync(CancellationToken.None);
                }
            }
        });
        images.Verify(
            service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public void RetryStore_DoesNotReuseKeyAcrossAccounts()
    {
        var store = new InMemoryAiRetryKeyStore();
        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");
        string first = store.GetOrCreate(job, "account-a", out bool firstRepeat);
        string second = store.GetOrCreate(job, "account-b", out bool secondRepeat);

        Assert.Multiple(() =>
        {
            Assert.That(firstRepeat, Is.False);
            Assert.That(secondRepeat, Is.False);
            Assert.That(second, Is.Not.EqualTo(first));
        });
    }

    [Test]
    public async Task PersistedRetryBypassesCurrentBalanceModelAndAvailabilityPreflight()
    {
        var store = new InMemoryAiRetryKeyStore();
        AiRetryAttemptContext context = RetryContext(store);
        AiJob job = Job("image", "{\"prompt\":\"a harbor\",\"aspectRatio\":\"1:1\"}") with
        {
            Model = new AiModelId("removed/model"),
        };
        store.GetOrCreate(job, "test-account", out _);
        var handler = new AiImageJobRetryHandler(
            Mock.Of<IAiImageGenerationService>(),
            EntitlementService(),
            AvailabilityService(available: false),
            ModelCatalogService(AiModelCatalog.Empty),
            context);

        AiJobRetryPreflight preflight = await handler.GetPreflightAsync(
            job,
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(preflight.CanSubmit, Is.True);
            Assert.That(preflight.IsAvailable, Is.False);
            Assert.That(preflight.Explanation, Is.EqualTo(Strings.AiResultUnavailable));
        });
    }

    [Test]
    public async Task RetiredRecoveryConfirmationDoesNotIssueAnotherPaidRequest()
    {
        var images = new Mock<IAiImageGenerationService>();
        var store = new InMemoryAiRetryKeyStore();
        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");
        store.GetOrCreate(job, "test-account", out _);
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(true),
            ModelCatalogService(),
            RetryContext(store));

        AiJobRetryPreparationResult prepared = await handler.PrepareAsync(job, CancellationToken.None);
        IAiJobRetryPreparation preparation = prepared.TakePreparation();
        store.Retire(job, "test-account");

        Assert.ThrowsAsync<AiJobRetryPreparationRejectedException>(() =>
            preparation.ExecuteAsync(CancellationToken.None));
        await preparation.DisposeAsync();
        await prepared.DisposeAsync();
        images.Verify(
            service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.That(store.TryGet(job, "test-account", out _), Is.False);
    }

    [Test]
    public async Task RecoveryConfirmationRejectsAnAuthenticatedAccountSwitch()
    {
        var images = new Mock<IAiImageGenerationService>();
        var store = new InMemoryAiRetryKeyStore();
        string account = "account-a";
        AiJob job = Job("image", "{\"prompt\":\"a harbor\"}");
        string original = store.GetOrCreate(job, account, out _);
        var context = new AiRetryAttemptContext(
            store,
            () => new AiAuthenticatedRequestIdentity(account, User: null),
            allowSyntheticIdentity: true);
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(true),
            ModelCatalogService(),
            context);

        AiJobRetryPreparationResult prepared = await handler.PrepareAsync(job, CancellationToken.None);
        IAiJobRetryPreparation preparation = prepared.TakePreparation();
        account = "account-b";

        Assert.ThrowsAsync<AiJobRetryPreparationRejectedException>(() =>
            preparation.ExecuteAsync(CancellationToken.None));
        await preparation.DisposeAsync();
        await prepared.DisposeAsync();
        images.Verify(
            service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.That(store.TryGet(job, "account-a", out string retained), Is.True);
        Assert.That(retained, Is.EqualTo(original));
    }

    [Test]
    public void ProductionRetryContextRejectsSyntheticIdentity()
    {
        var context = new AiRetryAttemptContext(
            new InMemoryAiRetryKeyStore(),
            () => new AiAuthenticatedRequestIdentity("account-a", User: null));

        Assert.Throws<AuthenticationRequiredException>(() => context.GetRequiredIdentity());
    }

    [TestCase("""{"prompt":"a harbor","aspectRatio":"16:9"}""", "16:9")]
    [TestCase("""{"prompt":"a harbor","size":"1536x1024"}""", "3:2")]
    [TestCase("""{"prompt":"a harbor","size":"1024x1536"}""", "2:3")]
    [TestCase("""{"prompt":"a harbor","size":"1024x1024"}""", "1:1")]
    [TestCase("""{"prompt":"a harbor"}""", "1:1")]
    public async Task ImageRetry_ReproducesTheShapeTheJobWasAskedFor(
        string inputParameters,
        string expectedAspectRatio)
    {
        var images = new Mock<IAiImageGenerationService>();
        AiImageGenerationRequest? sent = null;
        images
            .Setup(service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiImageGenerationRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(() => null!);

        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService(),
            RetryContext());

        await RunRetryAsync(handler, Job("image", inputParameters));

        Assert.That(sent!.AspectRatio.Value, Is.EqualTo(expectedAspectRatio));
    }

    [Test]
    public void ImageRetry_RefusesAGenerationGuidedByAReferenceImage()
    {
        var handler = new AiImageJobRetryHandler(
            Mock.Of<IAiImageGenerationService>(),
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService(),
            RetryContext());

        // The picture itself was never retained, so repeating this would make
        // something else and charge the same price for it.
        Assert.ThrowsAsync<InvalidOperationException>(() => RunRetryAsync(
            handler,
            Job("image", """{"prompt":"a harbor","aspectRatio":"1:1","reference":{"filename":"style.png"}}""")));
    }

    [Test]
    public async Task VideoRetry_CarriesTheShapeAudioAndSeedItRanWith()
    {
        var videos = new Mock<IAiVideoService>();
        AiVideoGenerationRequest? sent = null;
        videos
            .Setup(service => service.CreateAsync(
                It.IsAny<AiVideoGenerationRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiVideoGenerationRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(() => null!);

        var handler = new AiVideoJobRetryHandler(
            videos.Object,
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService(),
            RetryContext());

        await RunRetryAsync(
            handler,
            Job(
                "video",
                """
                {"prompt":"waves","durationSeconds":8,"resolution":"1080p",
                 "aspectRatio":"9:16","generateAudio":false,"seed":7}
                """));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent!.AspectRatio.Value, Is.EqualTo("9:16"));
            Assert.That(sent.GenerateAudio, Is.False);
            Assert.That(sent.Seed, Is.EqualTo(7));
            Assert.That(sent.Resolution.Value, Is.EqualTo("1080p"));
        }
    }

    [Test]
    public async Task VideoRetry_DefaultsMatchWhatTheEndpointAppliesToAnOlderJob()
    {
        var videos = new Mock<IAiVideoService>();
        AiVideoGenerationRequest? sent = null;
        videos
            .Setup(service => service.CreateAsync(
                It.IsAny<AiVideoGenerationRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<AiVideoGenerationRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(() => null!);

        var handler = new AiVideoJobRetryHandler(
            videos.Object,
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService(),
            RetryContext());

        // Recorded before these fields existed: repeating it must run it the
        // way it originally ran.
        await RunRetryAsync(
            handler,
            Job("video", """{"prompt":"waves"}"""));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent!.AspectRatio.Value, Is.EqualTo("16:9"));
            Assert.That(sent.GenerateAudio, Is.True);
            Assert.That(sent.Seed, Is.Null);
            Assert.That(sent.DurationSeconds, Is.EqualTo(4));
            Assert.That(sent.Resolution.Value, Is.EqualTo("720p"));
        }
    }

    [Test]
    public void VideoRetry_CanRetryRejectsSourceFramesAndMalformedInput()
    {
        var handler = new AiVideoJobRetryHandler(
            Mock.Of<IAiVideoService>(),
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService(),
            RetryContext());
        AiJobStatusSemantics failed = new(
            isTerminal: true,
            shouldPoll: false,
            outcome: AiJobOutcomes.Failed);

        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\"waves\",\"firstFrame\":{\"filename\":\"a.png\",\"mimeType\":\"image/png\"}}"), failed), Is.False);
        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\"waves\",\"durationSeconds\":\"four\"}"), failed), Is.False);
        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\"waves\",\"seed\":-1}"), failed), Is.False);
        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\" waves \"}"), failed), Is.False);
        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\"" + new string('x', 4001) + "\"}"), failed), Is.False);
        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\"waves\",\"ignored\":true}"), failed), Is.False);
        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\"waves\",\"aspectRatio\":\"3:2\"}"), failed), Is.False);
        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\"waves\",\"aspectRatio\":\"3:4\"}"), failed), Is.True);
        Assert.That(handler.CanRetry(Job("video", "{\"prompt\":\"waves\"}"), failed), Is.True);
    }

    [Test]
    public void VideoRetry_RefusesAClipConditionedOnSourceFrames()
    {
        var handler = new AiVideoJobRetryHandler(
            Mock.Of<IAiVideoService>(),
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService(),
            RetryContext());

        Assert.ThrowsAsync<InvalidOperationException>(() => RunRetryAsync(handler,
            Job(
                "video",
                """
                {"prompt":"waves","durationSeconds":4,"resolution":"720p",
                 "firstFrame":{"filename":"a.png","mimeType":"image/png"}}
                """)));
    }

    private static AiJob Job(string kind, string inputParameters) => new(
        new AiJobId("job-1"),
        new AiJobKindId(kind),
        new AiJobStatusId("failed"),
        JsonDocument.Parse(inputParameters).RootElement.Clone(),
        FileId: null,
        ContentUri: null,
        Error: "aiProviderError",
        CanRetry: true,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    private static async Task RunRetryAsync(IAiJobRetryHandler handler, AiJob job)
    {
        AiJobRetryPreflight preflight = await handler.GetPreflightAsync(job, CancellationToken.None);
        if (!preflight.CanSubmit)
            throw new AiRetryAttemptRejectedException();

        AiJobRetryPreparationResult prepared = await handler.PrepareAsync(
            job,
            CancellationToken.None);
        await using (prepared)
        {
            if (!prepared.IsReady)
                throw new AiRetryAttemptRejectedException();
            IAiJobRetryPreparation preparation = prepared.TakePreparation();
            await using (preparation)
            {
                await preparation.ExecuteAsync(CancellationToken.None);
            }
        }
    }

    private static IAiEntitlementService EntitlementService()
    {
        var mock = new Mock<IAiEntitlementService>();
        mock
            .Setup(service => service.RefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiEntitlements(
                Plan: "pro",
                SubscriptionStatus: "active",
                CurrentPeriodStart: null,
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: false,
                CanUseAi: true,
                Balance: new AiBalance(
                    new AiMonthlyUsage(0, 100, IsExhausted: false),
                    AdditionalCredits: 100,
                    HasAdditionalCreditDebt: false),
                Availability: new AiOperationAvailability([])));
        return mock.Object;
    }

    // An empty catalog is what a client that could not read the model list
    // holds, and it says nothing about any model, so these tests exercise the
    // retry path itself rather than the model check.
    private static IAiModelCatalogService ModelCatalogService(AiModelCatalog? catalog = null)
    {
        var mock = new Mock<IAiModelCatalogService>();
        mock
            .Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog ?? AiModelCatalog.Empty);
        return mock.Object;
    }

    private static IAiOperationAvailabilityService AvailabilityService(bool available)
    {
        var mock = new Mock<IAiOperationAvailabilityService>();
        mock
            .Setup(service => service.CheckAsync(
                It.IsAny<AiOperationAvailabilityRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(available);
        return mock.Object;
    }

    private static AiRetryAttemptContext RetryContext(
        IAiRetryKeyStore? store = null,
        string? accountId = "test-account")
        => new(
            store ?? new InMemoryAiRetryKeyStore(),
            () => accountId is null
                ? null
                : new AiAuthenticatedRequestIdentity(accountId, User: null),
            allowSyntheticIdentity: true);
}
