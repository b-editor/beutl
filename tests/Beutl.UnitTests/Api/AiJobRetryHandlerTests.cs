using System.Text.Json;
using Beutl.Api.Services;
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
            ModelCatalogService());

        await handler.RetryAsync(Job("image", inputParameters), CancellationToken.None);

        Assert.That(sent!.AspectRatio.Value, Is.EqualTo(expectedAspectRatio));
    }

    [Test]
    public void ImageRetry_RefusesAGenerationGuidedByAReferenceImage()
    {
        var handler = new AiImageJobRetryHandler(
            Mock.Of<IAiImageGenerationService>(),
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService());

        // The picture itself was never retained, so repeating this would make
        // something else and charge the same price for it.
        Assert.ThrowsAsync<InvalidOperationException>(() => handler.RetryAsync(
            Job("image", """{"prompt":"a harbor","aspectRatio":"1:1","reference":{"filename":"style.png"}}"""),
            CancellationToken.None));
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
            ModelCatalogService());

        await handler.RetryAsync(
            Job(
                "video",
                """
                {"prompt":"waves","durationSeconds":8,"resolution":"1080p",
                 "aspectRatio":"9:16","generateAudio":false,"seed":7}
                """),
            CancellationToken.None);

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
            ModelCatalogService());

        // Recorded before these fields existed: repeating it must run it the
        // way it originally ran.
        await handler.RetryAsync(
            Job("video", """{"prompt":"waves","durationSeconds":4,"resolution":"720p"}"""),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent!.AspectRatio.Value, Is.EqualTo("16:9"));
            Assert.That(sent.GenerateAudio, Is.True);
            Assert.That(sent.Seed, Is.Null);
        }
    }

    [Test]
    public void VideoRetry_RefusesAClipConditionedOnSourceFrames()
    {
        var handler = new AiVideoJobRetryHandler(
            Mock.Of<IAiVideoService>(),
            EntitlementService(),
            AvailabilityService(available: true),
            ModelCatalogService());

        Assert.ThrowsAsync<InvalidOperationException>(() => handler.RetryAsync(
            Job(
                "video",
                """
                {"prompt":"waves","durationSeconds":4,"resolution":"720p",
                 "firstFrame":{"filename":"a.png","mimeType":"image/png"}}
                """),
            CancellationToken.None));
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

    private static IAiEntitlementService EntitlementService() => Mock.Of<IAiEntitlementService>();

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
}
