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
