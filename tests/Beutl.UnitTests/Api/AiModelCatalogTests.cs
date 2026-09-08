using System.Collections.Immutable;
using System.Text.Json;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Language;
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
    [TestCase("", false, TestName = "Omitted image model dimension is unspecified")]
    [TestCase("\"aspectRatios\":null", false, TestName = "Null image model dimension is unspecified")]
    [TestCase("\"aspectRatios\":[]", true, TestName = "Empty image model dimension is unsupported")]
    public void CapabilityJson_PreservesOmittedNullAndEmptyImageModelDimensions(string dimensionJson, bool unsupported)
    {
        string model = "{\"id\":\"m\"" + (string.IsNullOrEmpty(dimensionJson) ? "" : "," + dimensionJson) + "}";
        string json = "{\"operations\":{\"image.generate\":{\"models\":[" + model + "]}}}";
        AiCapabilitiesResponse response = JsonSerializer.Deserialize<AiCapabilitiesResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        AiImageModelCapabilities image = AiModelMapper.ToModel(response).ModelsFor(AiOperations.ImageGeneration)[0].Image!;
        Assert.That(
            image.AspectRatios,
            Is.EqualTo(unsupported
                ? AiCapabilityDimension<string>.Unsupported
                : AiCapabilityDimension<string>.Unspecified));
    }

    [TestCase("", false, TestName = "Omitted image operation dimension is unspecified")]
    [TestCase("\"aspectRatios\":null", false, TestName = "Null image operation dimension is unspecified")]
    [TestCase("\"aspectRatios\":[]", true, TestName = "Empty image operation dimension is unsupported")]
    public void CapabilityJson_PreservesOmittedNullAndEmptyImageOperationDimensions(string dimensionJson, bool unsupported)
    {
        string operationProperties = string.IsNullOrEmpty(dimensionJson) ? "" : dimensionJson + ",";
        string json = "{\"operations\":{\"image.generate\":{" + operationProperties + "\"models\":[{\"id\":\"m\"}]}}}";
        AiCapabilitiesResponse response = JsonSerializer.Deserialize<AiCapabilitiesResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        AiImageModelCapabilities image = AiModelMapper.ToModel(response).ModelsFor(AiOperations.ImageGeneration)[0].Image!;
        Assert.That(
            image.AspectRatios,
            Is.EqualTo(unsupported
                ? AiCapabilityDimension<string>.Unsupported
                : AiCapabilityDimension<string>.Unspecified));
    }

    [TestCase("", false, TestName = "Omitted video model dimension is unspecified")]
    [TestCase("\"resolutions\":null", false, TestName = "Null video model dimension is unspecified")]
    [TestCase("\"resolutions\":[]", true, TestName = "Empty video model dimension is unsupported")]
    public void CapabilityJson_PreservesOmittedNullAndEmptyVideoModelDimensions(string dimensionJson, bool unsupported)
    {
        string model = "{\"id\":\"m\"" + (string.IsNullOrEmpty(dimensionJson) ? "" : "," + dimensionJson) + "}";
        string json = "{\"operations\":{\"video.generate\":{\"models\":[" + model + "]}}}";
        AiCapabilitiesResponse response = JsonSerializer.Deserialize<AiCapabilitiesResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        AiVideoModelCapabilities video = AiModelMapper.ToModel(response).ModelsFor(AiOperations.VideoGeneration)[0].Video!;
        Assert.That(
            video.Resolutions,
            Is.EqualTo(unsupported
                ? AiCapabilityDimension<string>.Unsupported
                : AiCapabilityDimension<string>.Unspecified));
    }

    [TestCase("", false, TestName = "Omitted video operation dimension is unspecified")]
    [TestCase("\"resolutions\":null", false, TestName = "Null video operation dimension is unspecified")]
    [TestCase("\"resolutions\":[]", true, TestName = "Empty video operation dimension is unsupported")]
    public void CapabilityJson_PreservesOmittedNullAndEmptyVideoOperationDimensions(string dimensionJson, bool unsupported)
    {
        string operationProperties = string.IsNullOrEmpty(dimensionJson) ? "" : dimensionJson + ",";
        string json = "{\"operations\":{\"video.generate\":{" + operationProperties + "\"models\":[{\"id\":\"m\"}]}}}";
        AiCapabilitiesResponse response = JsonSerializer.Deserialize<AiCapabilitiesResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        AiVideoModelCapabilities video = AiModelMapper.ToModel(response).ModelsFor(AiOperations.VideoGeneration)[0].Video!;
        Assert.That(
            video.Resolutions,
            Is.EqualTo(unsupported
                ? AiCapabilityDimension<string>.Unsupported
                : AiCapabilityDimension<string>.Unspecified));
    }
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
            Assert.That(catalog.OffersNoModel(AiOperations.ImageGeneration), Is.True);
            Assert.That(catalog.DefaultFor(AiOperations.ImageGeneration), Is.Null);
        }
    }

    [Test]
    public void Catalog_NormalizesDefaultModelArraysFromCustomProviders()
    {
        var operation = new AiOperationId("vendor.default-array");
        var catalog = new AiModelCatalog(
        [
            KeyValuePair.Create(operation, default(ImmutableArray<AiModelOption>)),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.ModelsFor(operation), Is.Empty);
            Assert.That(catalog.Operations[operation].IsDefault, Is.False);
        });
    }

    [Test]
    public void Catalog_RejectsDefaultModelIdsFromCustomProviders()
    {
        var operation = new AiOperationId("vendor.default-model-id");

        Assert.Throws<ArgumentException>(() => new AiModelCatalog(
        [
            KeyValuePair.Create(
                operation,
                ImmutableArray.Create(new AiModelOption(
                    default,
                    "Missing ID",
                    null,
                    IsDefault: true))),
        ]));
    }

    [Test]
    public void Catalog_RejectsDuplicateModelIdsWithinAnOperation()
    {
        var operation = new AiOperationId("vendor.duplicate-model-id");
        var modelId = new AiModelId("same-model");

        Assert.Throws<ArgumentException>(() => new AiModelCatalog(
        [
            KeyValuePair.Create(
                operation,
                ImmutableArray.Create(
                    new AiModelOption(modelId, "First", AiModelCostTier.Low, IsDefault: true),
                    new AiModelOption(modelId, "Second", AiModelCostTier.High, IsDefault: false))),
        ]));
    }

    [TestCase(0)]
    [TestCase(61)]
    public void Catalog_RejectsInvalidVideoDurationsFromCustomProviders(int durationSeconds)
    {
        var operation = new AiOperationId("vendor.invalid-video-duration");

        Assert.Throws<ArgumentException>(() => new AiModelCatalog(
        [
            KeyValuePair.Create(
                operation,
                ImmutableArray.Create(new AiModelOption(
                    new AiModelId("vendor/video"),
                    "Vendor Video",
                    AiModelCostTier.Medium,
                    IsDefault: true,
                    Video: new AiVideoModelCapabilities(
                        AiCapabilityDimension<int>.Supported([durationSeconds]),
                        AiCapabilityDimension<string>.Unspecified,
                        AiCapabilityDimension<string>.Unspecified,
                        SupportsAudio: true,
                        SupportsSeed: true)))),
        ]));
    }

    [Test]
    public void Catalog_TreatsAFullyFilteredModelListAsExplicitlyUnavailable()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("image.generate",
            [
                new AiModelDescriptionResponse { Id = string.Empty },
                new AiModelDescriptionResponse { Id = "   " },
            ])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.ModelsFor(AiOperations.ImageGeneration), Is.Empty);
            Assert.That(catalog.OffersNoModel(AiOperations.ImageGeneration), Is.True);
            Assert.That(catalog.DefaultFor(AiOperations.ImageGeneration), Is.Null);
        }
    }

    [Test]
    public void Catalog_KeepsAnOmittedModelListAsPermissiveLegacyFallback()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new() { Models = null },
            }.ToImmutableDictionary(),
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.ModelsFor(AiOperations.ImageGeneration), Is.Empty);
            Assert.That(catalog.OffersNoModel(AiOperations.ImageGeneration), Is.False);
            Assert.That(catalog.DefaultFor(AiOperations.ImageGeneration), Is.Null);
        }
    }

    [Test]
    public void Catalog_CustomVideoSchemaPreservesPublishedVideoCapabilities()
    {
        var extensions = new ExtensionProvider();
        using var schemas = new AiOperationCapabilitySchemaRegistry(extensions);
        var operation = new AiOperationId("vendor.video");
        extensions.AddExtensions(1,
        [
            new TestCapabilitySchemaExtension(
                new AiOperationCapabilitySchemaRegistration(
                    operation,
                    AiModelCapabilitySchema.Video)),
        ]);
        var response = new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [operation.Value] = new()
                {
                    Models = [new AiModelDescriptionResponse
                    {
                        Id = "vendor/video-model",
                        IsDefault = true,
                        DurationsSeconds = [4, 8],
                        Resolutions = ["720p"],
                        AspectRatios = ["16:9"],
                        Audio = true,
                        Seed = true,
                        FirstFrame = true,
                        LastFrame = false,
                    }],
                    MinDurationSeconds = 4,
                    MaxDurationSeconds = 8,
                    Resolutions = ["720p"],
                    AspectRatios = ["16:9"],
                },
            }.ToImmutableDictionary(),
        };

        AiModelCatalog catalog = AiModelMapper.ToModel(response, schemas);
        AiModelOption model = catalog.ModelsFor(operation).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.GetCapabilitySchema(operation),
                Is.EqualTo(AiModelCapabilitySchema.Video));
            Assert.That(model.Video, Is.Not.Null);
            Assert.That(model.Video!.DurationsSeconds.Values, Is.EqualTo(new[] { 4, 8 }));
            Assert.That(model.Video.Resolutions.Values, Is.EqualTo(new[] { "720p" }));
            Assert.That(model.Image, Is.Null);
        }
    }

    [Test]
    public void CapabilitySchemas_ComposeReplacementBeforeBaseAndRestoreFallbacks()
    {
        var extensions = new ExtensionProvider();
        using var schemas = new AiOperationCapabilitySchemaRegistry(extensions);
        var operation = new AiOperationId("vendor.replaced-shape");
        var replacement = new TestCapabilitySchemaExtension(
            new AiOperationCapabilitySchemaRegistration(
                operation,
                AiModelCapabilitySchema.Video,
                AiOperationCapabilitySchemaRegistrationMode.Replace));
        var @base = new TestCapabilitySchemaExtension(
            new AiOperationCapabilitySchemaRegistration(
                operation,
                AiModelCapabilitySchema.Image));

        extensions.AddExtensions(10, [replacement]);
        Assert.That(schemas.GetSchema(operation), Is.EqualTo(AiModelCapabilitySchema.Generic));

        extensions.AddExtensions(11, [@base]);
        Assert.That(schemas.GetSchema(operation), Is.EqualTo(AiModelCapabilitySchema.Video));

        extensions.RemoveExtensions(10);
        Assert.That(schemas.GetSchema(operation), Is.EqualTo(AiModelCapabilitySchema.Image));

        extensions.RemoveExtensions(11);
        Assert.That(schemas.GetSchema(operation), Is.EqualTo(AiModelCapabilitySchema.Generic));

        var builtInReplacement = new TestCapabilitySchemaExtension(
            new AiOperationCapabilitySchemaRegistration(
                AiOperations.VideoGeneration,
                AiModelCapabilitySchema.Image,
                AiOperationCapabilitySchemaRegistrationMode.Replace));
        extensions.AddExtensions(12, [builtInReplacement]);
        Assert.That(
            schemas.GetSchema(AiOperations.VideoGeneration),
            Is.EqualTo(AiModelCapabilitySchema.Image));

        extensions.RemoveExtensions(12);
        Assert.That(
            schemas.GetSchema(AiOperations.VideoGeneration),
            Is.EqualTo(AiModelCapabilitySchema.Video));
    }

    [Test]
    public void CapabilitySchemas_ReevaluateHealthyContributorAfterAtomicRejection()
    {
        var extensions = new ExtensionProvider();
        var reportedFailures = new List<Exception>();
        using var schemas = new AiOperationCapabilitySchemaRegistry(
            extensions,
            (_, exception) => reportedFailures.Add(exception));
        var operation = new AiOperationId("vendor.healthy-shape");
        var invalid = new TestCapabilitySchemaExtension(
            new AiOperationCapabilitySchemaRegistration(
                operation,
                AiModelCapabilitySchema.Image),
            new AiOperationCapabilitySchemaRegistration(
                new AiOperationId("vendor.missing-shape"),
                AiModelCapabilitySchema.Video,
                AiOperationCapabilitySchemaRegistrationMode.Replace));
        var healthy = new TestCapabilitySchemaExtension(
            new AiOperationCapabilitySchemaRegistration(
                operation,
                AiModelCapabilitySchema.Video));

        extensions.AddExtensions(13, [invalid]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(schemas.GetSchema(operation),
                Is.EqualTo(AiModelCapabilitySchema.Generic));
            Assert.That(reportedFailures, Has.Count.EqualTo(1));
        }

        reportedFailures.Clear();
        extensions.AddExtensions(14, [healthy]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(schemas.GetSchema(operation), Is.EqualTo(AiModelCapabilitySchema.Video));
            Assert.That(reportedFailures, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CapabilitySchemas_SerializeInitialSnapshotWithConcurrentRemoval()
    {
        var extensions = new ExtensionProvider();
        var operation = new AiOperationId("vendor.blocking-schema");
        var extension = new BlockingCapabilitySchemaExtension(
            new AiOperationCapabilitySchemaRegistration(
                operation,
                AiModelCapabilitySchema.Video));
        extensions.AddExtensions(15, [extension]);
        Task<AiOperationCapabilitySchemaRegistry> construction = Task.Run(
            () => new AiOperationCapabilitySchemaRegistry(extensions));
        await extension.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task removal = Task.Run(async () =>
        {
            ExtensionRemoval ticket = extensions.RemoveExtensions(15);
            await ticket.DrainAsync();
            foreach (Extension removed in ticket.Extensions)
                removed.Unload();
        });

        try
        {
            await Task.Delay(50);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(removal.IsCompleted, Is.False);
                Assert.That(extension.UnloadCount, Is.Zero);
            }
        }
        finally
        {
            extension.ReleaseRead.TrySetResult();
        }

        using AiOperationCapabilitySchemaRegistry schemas =
            await construction.WaitAsync(TimeSpan.FromSeconds(5));
        await removal.WaitAsync(TimeSpan.FromSeconds(5));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(schemas.GetSchema(operation),
                Is.EqualTo(AiModelCapabilitySchema.Generic));
            Assert.That(extension.UnloadCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void Catalog_CustomImageAndGenericSchemasDoNotInferFromSharedFields()
    {
        var extensions = new ExtensionProvider();
        using var schemas = new AiOperationCapabilitySchemaRegistry(extensions);
        var imageOperation = new AiOperationId("vendor.picture");
        var genericOperation = new AiOperationId("vendor.shared-fields");
        extensions.AddExtensions(1,
        [
            new TestCapabilitySchemaExtension(
                new AiOperationCapabilitySchemaRegistration(
                    imageOperation,
                    AiModelCapabilitySchema.Image)),
        ]);
        var response = new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [imageOperation.Value] = new()
                {
                    Models = [new AiModelDescriptionResponse
                    {
                        Id = "vendor/image-model",
                        IsDefault = true,
                        AspectRatios = ["1:1"],
                    }],
                    AspectRatios = ["1:1"],
                    MaxReferenceImagesTotalBytes = 7 * 1024 * 1024,
                },
                [genericOperation.Value] = new()
                {
                    Models = [new AiModelDescriptionResponse
                    {
                        Id = "vendor/generic-model",
                        IsDefault = true,
                        AspectRatios = ["1:1"],
                        Seed = true,
                    }],
                    MaxReferenceImagesTotalBytes = 19 * 1024 * 1024,
                },
            }.ToImmutableDictionary(),
        };

        AiModelCatalog catalog = AiModelMapper.ToModel(response, schemas);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.ModelsFor(imageOperation).Single().Image, Is.Not.Null);
            Assert.That(catalog.ModelsFor(imageOperation).Single().Video, Is.Null);
            Assert.That(catalog.GetImageReferenceLimits(imageOperation).MaxTotalBytes,
                Is.EqualTo(7 * 1024 * 1024));
            Assert.That(catalog.GetImageReferenceLimits(genericOperation),
                Is.EqualTo(AiImageReferenceLimits.Default));
            Assert.That(catalog.ImageReferenceLimitsByOperation.ContainsKey(genericOperation),
                Is.False);
            Assert.That(catalog.GetCapabilitySchema(genericOperation),
                Is.EqualTo(AiModelCapabilitySchema.Generic));
            Assert.That(catalog.ModelsFor(genericOperation).Single().Image, Is.Null);
            Assert.That(catalog.ModelsFor(genericOperation).Single().Video, Is.Null);
        }
    }

    [Test]
    public void Catalog_KeepsReferenceBudgetsScopedToEachImageOperation()
    {
        var extensions = new ExtensionProvider();
        using var schemas = new AiOperationCapabilitySchemaRegistry(extensions);
        var firstOperation = new AiOperationId("vendor.picture.small");
        var secondOperation = new AiOperationId("vendor.picture.large");
        extensions.AddExtensions(17,
        [
            new TestCapabilitySchemaExtension(
                new AiOperationCapabilitySchemaRegistration(
                    firstOperation,
                    AiModelCapabilitySchema.Image),
                new AiOperationCapabilitySchemaRegistration(
                    secondOperation,
                    AiModelCapabilitySchema.Image)),
        ]);
        var response = new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [firstOperation.Value] = new()
                {
                    Models = [new AiModelDescriptionResponse { Id = "vendor/small" }],
                    MaxReferenceImagesTotalBytes = 3 * 1024 * 1024,
                },
                [secondOperation.Value] = new()
                {
                    Models = [new AiModelDescriptionResponse { Id = "vendor/large" }],
                    MaxReferenceImagesTotalBytes = 15 * 1024 * 1024,
                },
            }.ToImmutableDictionary(),
        };

        AiModelCatalog catalog = AiModelMapper.ToModel(response, schemas);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.ImageReferenceLimitsByOperation.Keys,
                Is.EquivalentTo(new[] { firstOperation, secondOperation }));
            Assert.That(catalog.GetImageReferenceLimits(firstOperation).MaxTotalBytes,
                Is.EqualTo(3 * 1024 * 1024));
            Assert.That(catalog.GetImageReferenceLimits(secondOperation).MaxTotalBytes,
                Is.EqualTo(15 * 1024 * 1024));
        }
    }

    [Test]
    public void CapabilitySchemas_RejectUnsupportedEnumValuesAtPublicBoundaries()
    {
        var operation = new AiOperationId("vendor.invalid-shape");
        var invalid = (AiModelCapabilitySchema)int.MaxValue;

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AiOperationCapabilitySchemaRegistration(operation, invalid));
            Assert.Throws<ArgumentException>(() => new AiModelCatalog(
                [],
                capabilitySchemas: [KeyValuePair.Create(operation, invalid)]));
        }
    }

    [Test]
    public void Catalog_RejectsOperationsThatAlsoExplicitlyOfferNoModels()
    {
        var operation = new AiOperationId("vendor.contradictory");
        var model = new AiModelOption(
            new AiModelId("vendor/model"),
            "Model",
            null,
            true);

        Assert.Throws<ArgumentException>(() => new AiModelCatalog(
            [KeyValuePair.Create(operation, ImmutableArray.Create(model))],
            [operation]));
    }

    [Test]
    public void Catalog_AllowsCapabilitySchemaForAnOperationThatOffersNoModels()
    {
        var operation = new AiOperationId("vendor.disabled-video");

        var catalog = new AiModelCatalog(
            [],
            [operation],
            [KeyValuePair.Create(operation, AiModelCapabilitySchema.Video)]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.OffersNoModel(operation), Is.True);
            Assert.That(catalog.GetCapabilitySchema(operation),
                Is.EqualTo(AiModelCapabilitySchema.Video));
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
            Assert.That(video.DurationsSeconds.Values, Is.EqualTo(new[] { 5, 6, 7 }));
            Assert.That(video.Resolutions.Values, Is.EqualTo(new[] { "2K" }));
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
            Assert.That(video.DurationsSeconds.Values, Is.EqualTo(new[] { 4, 6, 8 }));
            Assert.That(video.Resolutions.Values, Is.EqualTo(new[] { "720p", "1080p" }));
            Assert.That(video.AspectRatios.Values, Is.EqualTo(new[] { "16:9" }));
        }
    }

    [Test]
    public void Catalog_LeavesAModelUnrestrictedWhenTheServerSaysNothing()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("video.generate", [Model("a/model", null, "low", isDefault: true)])));

        // A server that publishes no shapes is one this client asked before they
        // existed; typed Unspecified dimensions preserve that unrestricted state.
        AiVideoModelCapabilities video =
            catalog.ModelsFor(AiOperations.VideoGeneration)[0].Video!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(video.Resolutions.IsSpecified, Is.False);
            Assert.That(video.AspectRatios.IsSpecified, Is.False);
            Assert.That(video.DurationsSeconds.IsSpecified, Is.False);
            Assert.That(video.CanServeAnything(), Is.True);
        }
    }

    [Test]
    public void Capabilities_RuleOutAModelThatSharesNothingWithTheDialog()
    {
        var hailuo = new AiVideoModelCapabilities(
            AiCapabilityDimension<int>.Supported([5, 6]),
            AiCapabilityDimension<string>.Supported(["2K"]),
            AiCapabilityDimension<string>.Supported(["16:9"]),
            SupportsAudio: true,
            SupportsSeed: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hailuo.CanServeAnything(), Is.True);
            // Nothing left after narrowing means every request naming it would
            // be refused, so offering it is worse than hiding it.
            Assert.That((hailuo with { Resolutions = AiCapabilityDimension<string>.Unsupported }).CanServeAnything(), Is.False);
            Assert.That((hailuo with { AspectRatios = AiCapabilityDimension<string>.Unsupported }).CanServeAnything(), Is.False);
            Assert.That((hailuo with { DurationsSeconds = AiCapabilityDimension<int>.Unsupported }).CanServeAnything(), Is.False);
            Assert.That(AiVideoModelCapabilities.Unrestricted.CanServeAnything(), Is.True);
        }
    }

    [Test]
    public void CapabilityDimensions_NormalizeDefaultAndCompareByValue()
    {
        AiCapabilityDimension<string> omitted = default;
        AiCapabilityDimension<string> first =
            AiCapabilityDimension<string>.Supported(["720p", "1080p"]);
        AiCapabilityDimension<string> second =
            AiCapabilityDimension<string>.Supported(["720p", "1080p"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(omitted, Is.EqualTo(AiCapabilityDimension<string>.Unspecified));
            Assert.That(omitted.Values, Is.Empty);
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }
    }

    [Test]
    public void Catalog_ReadsValidatedRequestLimitSnapshots()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new()
                {
                    Models = [],
                    MaxReferenceImagesTotalBytes = 30L * 1024 * 1024,
                },
                [AiOperations.CaptionTranslation.Value] = new()
                {
                    Models = [],
                    MaxSegments = 150,
                    MaxCharacters = 12_000,
                    MaxRequestBytes = 96 * 1024,
                },
            }.ToImmutableDictionary(),
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.GetImageReferenceLimits(AiOperations.ImageGeneration).MaxTotalBytes,
                Is.EqualTo(30L * 1024 * 1024));
            Assert.That(catalog.CaptionTranslationLimits.MaxSegments, Is.EqualTo(150));
            Assert.That(catalog.CaptionTranslationLimits.MaxCharacters, Is.EqualTo(12_000));
            Assert.That(catalog.CaptionTranslationLimits.MaxRequestBytes, Is.EqualTo(96 * 1024));
            Assert.That(AiImageReferenceLimits.Default,
                Is.EqualTo(new AiImageReferenceLimits(AiRequestLimits.MaxImageReferencesTotalBytes)));
            Assert.That(AiCaptionTranslationLimits.Default,
                Is.EqualTo(new AiCaptionTranslationLimits(
                    AiRequestLimits.MaxTranslationSegments,
                    AiRequestLimits.MaxTranslationCharacters,
                    AiRequestLimits.MaxTranslationRequestBytes)));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new AiImageReferenceLimits(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ =
            new AiCaptionTranslationLimits(0, 1, 1));
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
            Assert.That(video.DurationsSeconds.Values, Is.Empty);
            Assert.That(video.DurationsSeconds.IsSpecified, Is.True);
            Assert.That(video.CanServeAnything(), Is.False);
        }
    }

    [Test]
    public void Catalog_FiltersExplicitVideoDurationsAndPreservesTriState()
    {
        AiModelDescriptionResponse omitted = VideoModel(
            "omitted/durations",
            durations: [4],
            resolutions: ["720p"],
            aspectRatios: ["16:9"],
            audio: true,
            seed: true) with
        {
            DurationsSeconds = null,
        };
        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("video.generate",
            [
                VideoModel(
                    "mixed/durations",
                    durations: [-1, 0, 1, 4, 60, 61],
                    resolutions: ["720p"],
                    aspectRatios: ["16:9"],
                    audio: true,
                    seed: true),
                VideoModel(
                    "invalid/durations",
                    durations: [-1, 0, 61],
                    resolutions: ["720p"],
                    aspectRatios: ["16:9"],
                    audio: true,
                    seed: true),
                omitted,
            ])));

        IReadOnlyDictionary<string, AiVideoModelCapabilities> videos = catalog
            .ModelsFor(AiOperations.VideoGeneration)
            .ToDictionary(option => option.Id.Value, option => option.Video!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(videos["mixed/durations"].DurationsSeconds.Values,
                Is.EqualTo(new[] { 1, 4, 60 }));
            Assert.That(videos["mixed/durations"].DurationsSeconds.IsSpecified, Is.True);
            Assert.That(videos["invalid/durations"].DurationsSeconds.Values, Is.Empty);
            Assert.That(videos["invalid/durations"].DurationsSeconds.IsSpecified, Is.True);
            Assert.That(videos["invalid/durations"].CanServeAnything(), Is.False);
            Assert.That(videos["omitted/durations"].DurationsSeconds.Values, Is.Empty);
            Assert.That(videos["omitted/durations"].DurationsSeconds.IsSpecified, Is.False);
        }
    }

    [Test]
    public void Catalog_UsesOperationBoundsWhenDurationListIsOmitted()
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
            Assert.That(video.DurationsSeconds.Values, Is.EqualTo(new[] { 4, 5, 6, 7, 8 }));
            Assert.That(video.DurationsSeconds.IsSpecified, Is.True);
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
            Assert.That(omittedResult.Resolutions.Values, Is.EqualTo(new[] { "720p", "1080p" }));
            Assert.That(omittedResult.AspectRatios.Values, Is.EqualTo(new[] { "16:9", "9:16" }));
            Assert.That(omittedResult.CanServeAnything(), Is.True);
            Assert.That(emptyResult.Resolutions.Values, Is.Empty);
            Assert.That(emptyResult.AspectRatios.Values, Is.Empty);
            Assert.That(emptyResult.CanServeAnything(), Is.False);
        }
    }

    [Test]
    public void Catalog_TreatsExplicitlyEmptyVideoOperationShapeAsUnsupported()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.VideoGeneration.Value] = new()
                {
                    Models = [VideoModel(
                        "empty/operation",
                        durations: [4],
                        resolutions: ["720p"],
                        aspectRatios: ["16:9"],
                        audio: true,
                        seed: true)],
                    Resolutions = [],
                    AspectRatios = [],
                },
            }.ToImmutableDictionary(),
        });

        AiVideoModelCapabilities video =
            catalog.ModelsFor(AiOperations.VideoGeneration)[0].Video!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(video.Resolutions, Is.EqualTo(AiCapabilityDimension<string>.Unsupported));
            Assert.That(video.AspectRatios, Is.EqualTo(AiCapabilityDimension<string>.Unsupported));
            Assert.That(video.CanServeAnything(), Is.False);
        }
    }

    [Test]
    public void Catalog_AppliesVideoOperationDimensionsWhenModelFieldsAreAllNull()
    {
        AiModelDescriptionResponse model = Model("legacy/video", null, "low", true);
        AiModelCatalog empty = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.VideoGeneration.Value] = new()
                {
                    Models = [model],
                    Resolutions = [],
                    AspectRatios = [],
                },
            }.ToImmutableDictionary(),
        });
        AiModelCatalog offered = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.VideoGeneration.Value] = new()
                {
                    Models = [model],
                    Resolutions = ["720p"],
                    AspectRatios = ["16:9"],
                },
            }.ToImmutableDictionary(),
        });
        AiModelCatalog omitted = AiModelMapper.ToModel(Capabilities(
            (AiOperations.VideoGeneration.Value, [model])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.ModelsFor(AiOperations.VideoGeneration)[0].Video!.CanServeAnything(), Is.False);
            Assert.That(offered.ModelsFor(AiOperations.VideoGeneration)[0].Video!.Resolutions.Values,
                Is.EqualTo(new[] { "720p" }));
            Assert.That(offered.ModelsFor(AiOperations.VideoGeneration)[0].Video!.AspectRatios.Values,
                Is.EqualTo(new[] { "16:9" }));
            Assert.That(omitted.ModelsFor(AiOperations.VideoGeneration)[0].Video!.Resolutions.IsSpecified,
                Is.False);
            Assert.That(omitted.ModelsFor(AiOperations.VideoGeneration)[0].Video!.AspectRatios.IsSpecified,
                Is.False);
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
            Assert.That(image.AspectRatios.Values, Is.EqualTo(new[] { "1:1", "3:2", "2:3" }));
            Assert.That(image.SupportsSeed, Is.False);
            Assert.That(image.MaxReferenceImages, Is.EqualTo(4));
            // The model publishes three and the operation takes the same three.
            Assert.That(
                image.Backgrounds.Values,
                Is.EqualTo(new[] { "auto", "opaque", "transparent" }));
        }
    }

    [Test]
    public void Catalog_DistinguishesNullAndEmptyImageDimensions()
    {
        AiModelDescriptionResponse omitted = new()
        {
            Id = "omitted/image",
            IsDefault = false,
            Seed = false,
            Resolution = false,
        };
        AiModelDescriptionResponse empty = new()
        {
            Id = "empty/image",
            IsDefault = false,
            AspectRatios = [],
            Backgrounds = [],
        };
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new()
                {
                    Models = [omitted, empty],
                    AspectRatios = ["1:1"],
                    Backgrounds = ["auto"],
                },
            }.ToImmutableDictionary(),
        });

        AiImageModelCapabilities omittedResult =
            catalog.ModelsFor(AiOperations.ImageGeneration)[0].Image!;
        AiImageModelCapabilities emptyResult =
            catalog.ModelsFor(AiOperations.ImageGeneration)[1].Image!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(omittedResult.AspectRatios.IsSpecified, Is.True);
            Assert.That(omittedResult.AspectRatios.Values, Is.EqualTo(new[] { "1:1" }));
            Assert.That(omittedResult.Backgrounds.IsSpecified, Is.True);
            Assert.That(omittedResult.Backgrounds.Values, Is.EqualTo(new[] { "auto" }));
            Assert.That(omittedResult.SupportsSeed, Is.False);
            Assert.That(omittedResult.SupportsResolution, Is.False);
            Assert.That(emptyResult.AspectRatios, Is.EqualTo(AiCapabilityDimension<string>.Unsupported));
            Assert.That(emptyResult.Backgrounds, Is.EqualTo(AiCapabilityDimension<string>.Unsupported));
            Assert.That(emptyResult.CanServeAnything(false), Is.False);
        }
    }

    [Test]
    public void Catalog_AppliesImageOperationDimensionsWhenModelFieldsAreAllNull()
    {
        AiModelDescriptionResponse model = Model("legacy/image", null, "low", true);
        AiModelCatalog empty = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new()
                {
                    Models = [model],
                    AspectRatios = [],
                    Backgrounds = [],
                },
            }.ToImmutableDictionary(),
        });
        AiModelCatalog offered = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new()
                {
                    Models = [model],
                    AspectRatios = ["1:1"],
                    Backgrounds = ["auto"],
                },
            }.ToImmutableDictionary(),
        });
        AiModelCatalog omitted = AiModelMapper.ToModel(Capabilities(
            (AiOperations.ImageGeneration.Value, [model])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.ModelsFor(AiOperations.ImageGeneration)[0].Image!.CanServeAnything(false), Is.False);
            Assert.That(offered.ModelsFor(AiOperations.ImageGeneration)[0].Image!.AspectRatios.Values,
                Is.EqualTo(new[] { "1:1" }));
            Assert.That(offered.ModelsFor(AiOperations.ImageGeneration)[0].Image!.Backgrounds.Values,
                Is.EqualTo(new[] { "auto" }));
            Assert.That(omitted.ModelsFor(AiOperations.ImageGeneration)[0].Image!.AspectRatios.IsSpecified,
                Is.False);
            Assert.That(omitted.ModelsFor(AiOperations.ImageGeneration)[0].Image!.Backgrounds.IsSpecified,
                Is.False);
        }
    }

    [Test]
    public void Catalog_AppliesVideoOperationDurationBoundsWhenModelDurationsAreUnspecified()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.VideoGeneration.Value] = new()
                {
                    Models = [Model("bounded/video", null, "low", true)],
                    MinDurationSeconds = 10,
                    MaxDurationSeconds = 12,
                },
            }.ToImmutableDictionary(),
        });

        AiCapabilityDimension<int> durations = catalog
            .ModelsFor(AiOperations.VideoGeneration)
            .Single().Video!.DurationsSeconds;

        Assert.That(durations.Values, Is.EqualTo(new[] { 10, 11, 12 }));
    }

    [Test]
    public void Catalog_LeavesImageDimensionUnspecifiedWhenOperationIsOmitted()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new()
                {
                    Models = [ImageModel(
                        "image/model",
                        aspectRatios: ["16:9"],
                        backgrounds: ["auto"],
                        seed: true,
                        maxReferenceImages: 1)],
                },
            }.ToImmutableDictionary(),
        });

        AiImageModelCapabilities image =
            catalog.ModelsFor(AiOperations.ImageGeneration)[0].Image!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.AspectRatios.Values, Is.EqualTo(new[] { "16:9" }));
            Assert.That(image.Backgrounds.Values, Is.EqualTo(new[] { "auto" }));
        }
    }

    [Test]
    public void Catalog_TreatsExplicitlyEmptyImageOperationDimensionsAsUnsupported()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new()
                {
                    Models = [new AiModelDescriptionResponse
                    {
                        Id = "empty/operation",
                        IsDefault = false,
                        Seed = false,
                    }],
                    AspectRatios = [],
                    Backgrounds = [],
                },
            }.ToImmutableDictionary(),
        });

        AiImageModelCapabilities image =
            catalog.ModelsFor(AiOperations.ImageGeneration)[0].Image!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.AspectRatios, Is.EqualTo(AiCapabilityDimension<string>.Unsupported));
            Assert.That(image.Backgrounds, Is.EqualTo(AiCapabilityDimension<string>.Unsupported));
            Assert.That(image.CanServeAnything(false), Is.False);
        }
    }

    [Test]
    public void Catalog_HidesImageModelWhenDimensionsDoNotOverlap()
    {
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new()
                {
                    Models = [ImageModel(
                        "image/model",
                        aspectRatios: ["16:9"],
                        backgrounds: ["transparent"],
                        seed: true,
                        maxReferenceImages: 1)],
                    AspectRatios = ["1:1"],
                    Backgrounds = ["opaque"],
                },
            }.ToImmutableDictionary(),
        });

        AiImageModelCapabilities image =
            catalog.ModelsFor(AiOperations.ImageGeneration)[0].Image!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.AspectRatios, Is.EqualTo(AiCapabilityDimension<string>.Unsupported));
            Assert.That(image.Backgrounds, Is.EqualTo(AiCapabilityDimension<string>.Unsupported));
            Assert.That(image.CanServeAnything(false), Is.False);
        }
    }

    [Test]
    public void Catalog_RecognizesImageModelWithOnlySeedOrResolution()
    {
        AiModelDescriptionResponse seedOnly = new()
        {
            Id = "seed-only",
            IsDefault = false,
            Seed = false,
        };
        AiModelDescriptionResponse resolutionOnly = new()
        {
            Id = "resolution-only",
            IsDefault = false,
            Resolution = false,
        };
        AiModelCatalog catalog = AiModelMapper.ToModel(new AiCapabilitiesResponse
        {
            Operations = new Dictionary<string, AiOperationCapabilityResponse>
            {
                [AiOperations.ImageGeneration.Value] = new()
                {
                    Models = [seedOnly, resolutionOnly],
                },
            }.ToImmutableDictionary(),
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.ModelsFor(AiOperations.ImageGeneration), Has.Length.EqualTo(2));
            Assert.That(catalog.ModelsFor(AiOperations.ImageGeneration)[0].Image!.SupportsSeed, Is.False);
            Assert.That(catalog.ModelsFor(AiOperations.ImageGeneration)[1].Image!.SupportsResolution, Is.False);
            Assert.That(catalog.ModelsFor(AiOperations.ImageGeneration)[0].Image!.AspectRatios.IsSpecified, Is.False);
            Assert.That(catalog.ModelsFor(AiOperations.ImageGeneration)[1].Image!.Backgrounds.IsSpecified, Is.False);
        }
    }

    [Test]
    public void ImageCapabilities_RuleOutAModelAnEditCannotHandAPictureTo()
    {
        var noReferences = new AiImageModelCapabilities(
            AiCapabilityDimension<string>.Supported(["1:1"]),
            Backgrounds: AiCapabilityDimension<string>.Supported(["auto"]),
            SupportsSeed: true,
            MaxReferenceImages: 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(noReferences.CanServeAnything(false), Is.True);
            // Every edit sends the picture being edited.
            Assert.That(noReferences.CanServeAnything(true), Is.False);
            Assert.That(
                (noReferences with
                {
                    AspectRatios = AiCapabilityDimension<string>.Unsupported,
                }).CanServeAnything(false),
                Is.False);
        }
    }

    [Test]
    public void ImageCapabilities_UnrestrictedHasNoContradictoryEmptyDimensions()
    {
        AiImageModelCapabilities unrestricted = AiImageModelCapabilities.Unrestricted;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unrestricted.CanServeAnything(false), Is.True);
            Assert.That(unrestricted.AspectRatios.IsSpecified, Is.False);
            Assert.That(unrestricted.Backgrounds.IsSpecified, Is.False);
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

        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("image.generate", [
                Model("cheap/model", null, "low", isDefault: true),
                Model("dear/model", null, "high", isDefault: false),
            ])));
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(),
            ModelCatalogService(catalog),
            RetryContext());

        Assert.DoesNotThrowAsync(() => RunRetryAsync(handler, ImageJob("dear/model")));
        // Not the default, which is cheaper and would produce a different picture.
        Assert.That(sent!.Model!.Value.Value, Is.EqualTo("dear/model"));
    }

    [Test]
    public async Task Retry_RefusesAModelTheServerNoLongerOffers()
    {
        var images = new Mock<IAiImageGenerationService>();
        AiModelCatalog catalog = AiModelMapper.ToModel(Capabilities(
            ("image.generate", [Model("cheap/model", null, "low", isDefault: true)])));
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(),
            ModelCatalogService(catalog),
            RetryContext());

        // Quietly rerunning on the default would charge the default's price for
        // a model the user never chose.
        AiJob job = ImageJob("withdrawn/model");
        AiJobRetryPreflight preflight = await handler.GetPreflightAsync(
            job,
            CancellationToken.None);
        await using AiJobRetryPreparationResult prepared = await handler.PrepareAsync(
            job,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preflight.CanSubmit, Is.False);
            Assert.That(prepared.IsReady, Is.False);
            Assert.That(prepared.Explanation, Is.EqualTo(Strings.AiModelUnavailable));
        }
        images.Verify(service => service.GenerateAsync(
            It.IsAny<AiImageGenerationRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Retry_RefusesAModelWhenTheOperationExplicitlyOffersNoModels()
    {
        var images = new Mock<IAiImageGenerationService>();
        var catalog = new AiModelCatalog(
            [],
            withoutModels: [AiOperations.ImageGeneration]);
        var handler = new AiImageJobRetryHandler(
            images.Object,
            EntitlementService(),
            AvailabilityService(),
            ModelCatalogService(catalog),
            RetryContext());
        AiJob job = ImageJob("withdrawn/model");

        AiJobRetryPreflight preflight = await handler.GetPreflightAsync(
            job,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(preflight.CanSubmit, Is.False);
            Assert.That(preflight.Explanation, Is.EqualTo(Strings.AiModelUnavailable));
        }
        images.Verify(service => service.GenerateAsync(
            It.IsAny<AiImageGenerationRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
            EntitlementService(),
            AvailabilityService(),
            ModelCatalogService(AiModelCatalog.Empty),
            RetryContext());

        // An empty catalog says nothing about any model, and the server has the
        // last word; refusing here would break reruns whenever the capabilities
        // endpoint is unreachable.
        Assert.DoesNotThrowAsync(() => RunRetryAsync(handler, ImageJob("dear/model")));
    }

    private static async Task RunRetryAsync(IAiJobRetryHandler handler, AiJob job)
    {
        AiJobRetryPreflight preflight = await handler.GetPreflightAsync(job, CancellationToken.None);
        if (!preflight.CanSubmit)
            throw new InvalidOperationException("Retry preflight blocked the request.");
        AiJobRetryPreparationResult prepared = await handler.PrepareAsync(job, CancellationToken.None);
        await using (prepared)
        {
            if (!prepared.IsReady)
                throw new InvalidOperationException("Retry preparation blocked the request.");
            IAiJobRetryPreparation preparation = prepared.TakePreparation();
            await using (preparation)
            {
                await preparation.ExecuteAsync(CancellationToken.None);
            }
        }
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

    private sealed class TestCapabilitySchemaExtension(
        params AiOperationCapabilitySchemaRegistration[] registrations)
        : AiOperationCapabilitySchemaExtension
    {
        public override IReadOnlyCollection<AiOperationCapabilitySchemaRegistration> Registrations
        { get; } = registrations;
    }

    private sealed class BlockingCapabilitySchemaExtension(
        params AiOperationCapabilitySchemaRegistration[] registrations)
        : AiOperationCapabilitySchemaExtension
    {
        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int UnloadCount { get; private set; }

        public override IReadOnlyCollection<AiOperationCapabilitySchemaRegistration> Registrations
        {
            get
            {
                ReadStarted.TrySetResult();
                ReleaseRead.Task.GetAwaiter().GetResult();
                return registrations;
            }
        }

        public override void Unload() => UnloadCount++;
    }

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

    private static IAiEntitlementService EntitlementService()
    {
        var mock = new Mock<IAiEntitlementService>();
        mock
            .Setup(service => service.RefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiEntitlements(
                "pro",
                "active",
                null,
                null,
                false,
                true,
                new AiBalance(new AiMonthlyUsage(0, 100, false), 100, false),
                new AiOperationAvailability([])));
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

    private static AiRetryAttemptContext RetryContext()
        => new(
            new InMemoryAiRetryKeyStore(),
            () => new AiAuthenticatedRequestIdentity("test-account", User: null),
            allowSyntheticIdentity: true);
}
