using System.Collections.Immutable;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Moq;
using NUnit.Framework;

namespace Beutl.UnitTests.Api;

/// <summary>
/// The models an operation offers are registered on the server, so unlike every
/// other list this client holds they arrive over the wire. These cover reading
/// that list and the one rule that costs money if it goes wrong: a rerun must
/// repeat the model the job ran on, or not run at all.
/// </summary>
public class AiModelCatalogTests
{
    [Test]
    public void Catalog_ReadsTheModelsAndTheirRelativeCost()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("image.generate", [
                Model("cheap/model", null, "low", isDefault: true),
                Model("dear/model", "Dear", "high", isDefault: false),
            ])));

        ImmutableArray<AiModelOption> models = catalog.ModelsFor(AiOperations.ImageGeneration);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(models.Select(model => model.Id.Value),
                Is.EqualTo(new[] { "cheap/model", "dear/model" }));
            // A model with no name of its own shows its id rather than nothing.
            Assert.That(models[0].DisplayName, Is.EqualTo("cheap/model"));
            Assert.That(models[1].DisplayName, Is.EqualTo("Dear"));
            Assert.That(models[0].CostTier, Is.EqualTo(AiModelCostTier.Low));
            Assert.That(models[1].CostTier, Is.EqualTo(AiModelCostTier.High));
            Assert.That(catalog.DefaultFor(AiOperations.ImageGeneration)!.Id.Value,
                Is.EqualTo("cheap/model"));
        }
    }

    [Test]
    public void Catalog_ReportsAnUnknownTierAsNoneRatherThanGuessing()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("video.generate", [Model("a/model", null, "cheapest", isDefault: true)])));

        // Guessing would send someone to the pricier model believing it is the
        // cheaper one.
        Assert.That(catalog.ModelsFor(AiOperations.VideoGeneration)[0].CostTier, Is.Null);
    }

    [Test]
    public void Catalog_IsEmptyForAnOperationTheServerDidNotDescribe()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("image.generate", [])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.ModelsFor(AiOperations.ImageGeneration), Is.Empty);
            // Nothing to choose means the request names no model and the server
            // uses its own default, as it did before models could be chosen.
            Assert.That(catalog.DefaultFor(AiOperations.ImageGeneration), Is.Null);
        }
    }

    [Test]
    public void Catalog_ReadsWhatEachVideoModelWillTake()
    {
        // What a video request may carry differs per model, and a fixed set of
        // options produces requests the server refuses after the usage is
        // reserved: MiniMax H3 renders only at 2K and takes nothing under five
        // seconds.
        AiModelCatalog catalog = AiModelMapper.ToModel(VideoCapabilities(
            resolutions: ["480p", "720p", "1080p", "2K"],
            aspectRatios: ["16:9", "9:16", "1:1"],
            minDurationSeconds: 1,
            maxDurationSeconds: 60,
            models: [
                VideoModel(
                    "minimax/hailuo-3",
                    durations: [5, 6, 7],
                    resolutions: ["2K"],
                    aspectRatios: ["16:9", "9:16"],
                    audio: true,
                    seed: false),
            ]));

        AiVideoModelCapabilities video =
            catalog.ModelsFor(AiOperations.VideoGeneration)[0].Video!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(video.DurationsSeconds, Is.EqualTo(new[] { 5, 6, 7 }));
            Assert.That(video.Resolutions, Is.EqualTo(new[] { "2K" }));
            Assert.That(video.SupportsAudio, Is.True);
            Assert.That(video.SupportsSeed, Is.False);
        }
    }

    [Test]
    public void Catalog_KeepsAVideoModelWithinWhatTheOperationAccepts()
    {
        // 4K is the model's; this client cannot ask the server for it, and a
        // resolution the server would refuse must never reach the dialog.
        AiModelCatalog catalog = AiModelMapper.ToModel(VideoCapabilities(
            resolutions: ["720p", "1080p"],
            aspectRatios: ["16:9"],
            minDurationSeconds: 4,
            maxDurationSeconds: 8,
            models: [
                VideoModel(
                    "bytedance/seedance-2.0",
                    durations: [4, 6, 8, 12],
                    resolutions: ["720p", "1080p", "4K"],
                    aspectRatios: ["16:9", "21:9"],
                    audio: true,
                    seed: true),
            ]));

        AiVideoModelCapabilities video =
            catalog.ModelsFor(AiOperations.VideoGeneration)[0].Video!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(video.DurationsSeconds, Is.EqualTo(new[] { 4, 6, 8 }));
            Assert.That(video.Resolutions, Is.EqualTo(new[] { "720p", "1080p" }));
            Assert.That(video.AspectRatios, Is.EqualTo(new[] { "16:9" }));
        }
    }

    [Test]
    public void Catalog_LeavesAModelUnrestrictedWhenTheServerSaysNothing()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("video.generate", [Model("a/model", null, "low", isDefault: true)])));

        // A server that publishes no shapes is one this client asked before they
        // existed; the dialog then offers what it always offered.
        Assert.That(catalog.ModelsFor(AiOperations.VideoGeneration)[0].Video, Is.Null);
    }

    [Test]
    public void Capabilities_RuleOutAModelThatSharesNothingWithTheDialog()
    {
        var hailuo = new AiVideoModelCapabilities(
            [5, 6],
            ["2K"],
            ["16:9"],
            SupportsAudio: true,
            SupportsSeed: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hailuo.CanServeAnything(), Is.True);
            // Nothing left after narrowing means every request naming it would
            // be refused, so offering it is worse than hiding it.
            Assert.That((hailuo with { Resolutions = [] }).CanServeAnything(), Is.False);
            Assert.That((hailuo with { AspectRatios = [] }).CanServeAnything(), Is.False);
            Assert.That((hailuo with { DurationsSeconds = [] }).CanServeAnything(), Is.False);
            Assert.That(AiVideoModelCapabilities.Unrestricted.CanServeAnything(), Is.True);
        }
    }

    [Test]
    public void Catalog_RulesOutAModelWithAnExplicitlyEmptyDurationList()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(VideoCapabilities(
            resolutions: ["720p"],
            aspectRatios: ["16:9"],
            minDurationSeconds: 4,
            maxDurationSeconds: 8,
            models:
            [
                VideoModel(
                    "empty/durations",
                    durations: [],
                    resolutions: ["720p"],
                    aspectRatios: ["16:9"],
                    audio: true,
                    seed: true),
            ]));

        AiVideoModelCapabilities video =
            catalog.ModelsFor(AiOperations.VideoGeneration).Single().Video!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(video.DurationsSeconds, Is.Empty);
            Assert.That(video.DurationsSeconds.IsDefault, Is.False);
            Assert.That(video.CanServeAnything(), Is.False);
        }
    }

    [Test]
    public void Catalog_LeavesAnOmittedDurationListUnrestricted()
    {
        AiModelDescriptionResponse model = VideoModel(
            "omitted/durations",
            durations: [4],
            resolutions: ["720p"],
            aspectRatios: ["16:9"],
            audio: true,
            seed: true) with
        {
            DurationsSeconds = null,
        };
        AiModelCatalog catalog = AiModelMapper.ToModel(VideoCapabilities(
            resolutions: ["720p"],
            aspectRatios: ["16:9"],
            minDurationSeconds: 4,
            maxDurationSeconds: 8,
            models: [model]));

        AiVideoModelCapabilities video =
            catalog.ModelsFor(AiOperations.VideoGeneration).Single().Video!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(video.DurationsSeconds.IsDefault, Is.True);
            Assert.That(video.CanServeAnything(), Is.True);
        }
    }

    [Test]
    public void Catalog_DistinguishesOmittedAndExplicitlyEmptyVideoShapes()
    {
        AiModelDescriptionResponse omitted = VideoModel(
            "omitted/shapes",
            durations: [4],
            resolutions: ["720p"],
            aspectRatios: ["16:9"],
            audio: true,
            seed: true) with
        {
            Resolutions = null,
            AspectRatios = null,
        };
        AiModelDescriptionResponse empty = VideoModel(
            "empty/shapes",
            durations: [4],
            resolutions: [],
            aspectRatios: [],
            audio: true,
            seed: true);
        AiModelCatalog catalog = AiModelMapper.ToModel(VideoCapabilities(
            resolutions: ["720p", "1080p"],
            aspectRatios: ["16:9", "9:16"],
            minDurationSeconds: 4,
            maxDurationSeconds: 8,
            models: [omitted, empty]));

        AiVideoModelCapabilities omittedResult = catalog.ModelsFor(AiOperations.VideoGeneration)[0].Video!;
        AiVideoModelCapabilities emptyResult = catalog.ModelsFor(AiOperations.VideoGeneration)[1].Video!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(omittedResult.Resolutions, Is.EqualTo(new[] { "720p", "1080p" }));
            Assert.That(omittedResult.AspectRatios, Is.EqualTo(new[] { "16:9", "9:16" }));
            Assert.That(omittedResult.CanServeAnything(), Is.True);
            Assert.That(emptyResult.Resolutions, Is.Empty);
            Assert.That(emptyResult.AspectRatios, Is.Empty);
            Assert.That(emptyResult.CanServeAnything(), Is.False);
        }
    }

    [Test]
    public void Catalog_ReadsWhatEachImageModelWillTake()
    {
        // GPT Image-1 renders 1:1, 3:2 and 2:3 and refuses everything else, so
        // a fixed set of shapes produces requests it rejects after the usage is
        // reserved. 21:9 is the model's; the operation does not offer it.
        AiModelCatalog catalog = AiModelMapper.ToModel(ImageCapabilities(
            aspectRatios: ["1:1", "16:9", "3:2", "2:3"],
            models: [
                ImageModel(
                    "openai/gpt-image-1",
                    aspectRatios: ["1:1", "3:2", "2:3", "21:9"],
                    backgrounds: ["auto", "opaque", "transparent"],
                    seed: false,
                    maxReferenceImages: 4),
            ]));

        AiImageModelCapabilities image =
            catalog.ModelsFor(AiOperations.ImageGeneration)[0].Image!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.AspectRatios, Is.EqualTo(new[] { "1:1", "3:2", "2:3" }));
            Assert.That(image.SupportsSeed, Is.False);
            Assert.That(image.MaxReferenceImages, Is.EqualTo(4));
            // The model publishes three and the operation takes the same three.
            Assert.That(
                image.Backgrounds,
                Is.EqualTo(new[] { "auto", "opaque", "transparent" }));
        }
    }

    [Test]
    public void ImageCapabilities_RuleOutAModelAnEditCannotHandAPictureTo()
    {
        var noReferences = new AiImageModelCapabilities(
            ["1:1"],
            Backgrounds: ["auto"],
            SupportsSeed: true,
            MaxReferenceImages: 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(noReferences.CanServeAnything(false), Is.True);
            // Every edit sends the picture being edited.
            Assert.That(noReferences.CanServeAnything(true), Is.False);
            Assert.That(
                (noReferences with { AspectRatios = [] }).CanServeAnything(false),
                Is.False);
        }
    }

    [Test]
    public void Retry_RepeatsTheModelTheJobRanOn()
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
            Mock.Of<IAiEntitlementService>(),
            AvailabilityService(),
            ModelCatalogService(AiModelMapper.ToModel(Capabilities(
                ("image.generate", [
                    Model("cheap/model", null, "low", isDefault: true),
                    Model("dear/model", null, "high", isDefault: false),
                ])))));

        Assert.DoesNotThrowAsync(() => handler.RetryAsync(
            ImageJob("dear/model"),
            CancellationToken.None));
        // Not the default, which is cheaper and would produce a different picture.
        Assert.That(sent!.Model!.Value.Value, Is.EqualTo("dear/model"));
    }

    [Test]
    public void Retry_RefusesAModelTheServerNoLongerOffers()
    {
        var handler = new AiImageJobRetryHandler(
            Mock.Of<IAiImageGenerationService>(),
            Mock.Of<IAiEntitlementService>(),
            AvailabilityService(),
            ModelCatalogService(AiModelMapper.ToModel(Capabilities(
                ("image.generate", [Model("cheap/model", null, "low", isDefault: true)])))));

        // Quietly rerunning on the default would charge the default's price for
        // a model the user never chose.
        Assert.ThrowsAsync<AiModelUnavailableException>(() => handler.RetryAsync(
            ImageJob("withdrawn/model"),
            CancellationToken.None));
    }

    [Test]
    public void Retry_ProceedsWhenTheCatalogCouldNotBeRead()
    {
        var images = new Mock<IAiImageGenerationService>();
        images
            .Setup(service => service.GenerateAsync(
                It.IsAny<AiImageGenerationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => null!);

        var handler = new AiImageJobRetryHandler(
            images.Object,
            Mock.Of<IAiEntitlementService>(),
            AvailabilityService(),
            ModelCatalogService(AiModelCatalog.Empty));

        // An empty catalog says nothing about any model, and the server has the
        // last word; refusing here would break reruns whenever the capabilities
        // endpoint is unreachable.
        Assert.DoesNotThrowAsync(() => handler.RetryAsync(
            ImageJob("dear/model"),
            CancellationToken.None));
    }

    private static AiJob ImageJob(string model) => new(
        new AiJobId("job-1"),
        AiJobKinds.Image,
        new AiJobStatusId("failed"),
        System.Text.Json.JsonDocument
            .Parse("""{"prompt":"a harbor","aspectRatio":"1:1"}""")
            .RootElement.Clone(),
        FileId: null,
        ContentUri: null,
        Error: "aiProviderError",
        CanRetry: true,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch)
    {
        Model = new AiModelId(model),
    };

    private static AiModelDescriptionResponse Model(
        string id,
        string? displayName,
        string? costTier,
        bool isDefault)
        => new()
        {
            Id = id,
            DisplayName = displayName,
            CostTier = costTier,
            IsDefault = isDefault,
        };

    private static AiModelDescriptionResponse VideoModel(
        string id,
        ImmutableArray<int> durations,
        ImmutableArray<string> resolutions,
        ImmutableArray<string> aspectRatios,
        bool audio,
        bool seed)
        => new()
        {
            Id = id,
            IsDefault = false,
            DurationsSeconds = durations,
            Resolutions = resolutions,
            AspectRatios = aspectRatios,
            Audio = audio,
            Seed = seed,
        };

    private static AiModelDescriptionResponse ImageModel(
        string id,
        ImmutableArray<string> aspectRatios,
        ImmutableArray<string> backgrounds,
        bool seed,
        int maxReferenceImages)
        => new()
        {
            Id = id,
            IsDefault = false,
            AspectRatios = aspectRatios,
            Backgrounds = backgrounds,
            Seed = seed,
            MaxReferenceImages = maxReferenceImages,
        };

    private static AiCapabilitiesResponse ImageCapabilities(
        ImmutableArray<string> aspectRatios,
        ImmutableArray<AiModelDescriptionResponse> models)
        => new()
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                ["image.generate"] = new()
                {
                    Models = models,
                    AspectRatios = aspectRatios,
                },
            }.ToImmutableDictionary(),
        };

    private static AiCapabilitiesResponse VideoCapabilities(
        ImmutableArray<string> resolutions,
        ImmutableArray<string> aspectRatios,
        int minDurationSeconds,
        int maxDurationSeconds,
        ImmutableArray<AiModelDescriptionResponse> models)
        => new()
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                ["video.generate"] = new()
                {
                    Models = models,
                    Resolutions = resolutions,
                    AspectRatios = aspectRatios,
                    MinDurationSeconds = minDurationSeconds,
                    MaxDurationSeconds = maxDurationSeconds,
                },
            }.ToImmutableDictionary(),
        };

    private static AiCapabilitiesResponse Capabilities(
        params (string Operation, ImmutableArray<AiModelDescriptionResponse> Models)[] operations)
        => new()
        {
            Operations = operations.ToImmutableDictionary(
                entry => entry.Operation,
                entry => new AiOperationCapabilityResponse { Models = entry.Models }),
        };

    private static IAiOperationAvailabilityService AvailabilityService()
    {
        var mock = new Mock<IAiOperationAvailabilityService>();
        mock
            .Setup(service => service.CheckAsync(
                It.IsAny<AiOperationAvailabilityRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock.Object;
    }

    private static IAiModelCatalogService ModelCatalogService(AiModelCatalog catalog)
    {
        var mock = new Mock<IAiModelCatalogService>();
        mock
            .Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalog);
        return mock.Object;
    }
}
