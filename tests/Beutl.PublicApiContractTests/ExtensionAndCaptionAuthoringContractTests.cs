using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Graphics;

namespace Beutl.PublicApiContractTests;

public sealed class ExtensionAndCaptionAuthoringContractTests
{
    [Test]
    public void ExtensionProviderSurface_ExposesMetadataAndLeaseOnly()
    {
        Type provider = typeof(IExtensionProvider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetProperty("AllExtensions"), Is.Null);
            Assert.That(provider.GetMethod("GetExtensions"), Is.Null);
            Assert.That(provider.GetMethod("MatchEditorExtension"), Is.Null);
            Assert.That(typeof(ExtensionProvider).GetProperty("AllExtensions"), Is.Null);
            Assert.That(typeof(ExtensionProvider).GetMethod("GetExtensions"), Is.Null);
            Assert.That(
                typeof(ExtensionDescriptor).GetProperties().Select(property => property.PropertyType),
                Does.Not.Contain(typeof(Type)).And.Not.Contain(typeof(Extension)));
        }
    }

    [Test]
    public void EveryExtensionKind_LivesInBeutlExtensibility()
    {
        Type[] kinds = [.. new[] { typeof(ExtensionProvider).Assembly, typeof(CaptionTemplateRegistry).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsAbstract && type.IsSubclassOf(typeof(Extension)))];

        Assert.That(kinds, Is.Empty);
    }

    [Test]
    public void MetadataTypes_CanBeCreatedByExternalProviderImplementations()
    {
        var extension = new ExtensionDescriptor(
            new ExtensionId(Guid.NewGuid()),
            "Example.Extension, Example.Package");
        IExtensionProvider provider = new MetadataExtensionProvider(extension);
        var captionFormat = new CaptionFormatId("example");
        var codecDescriptor = new CaptionCodecDescriptor(
            captionFormat,
            [".example"],
            order: 20);
        var codec = new CaptionCodecInfo(
            captionFormat,
            [".example"],
            canDecode: true,
            canEncode: false,
            order: 20);
        var template = new CaptionTemplateDescriptor(
            new CaptionTemplateId("example.template"),
            new CaptionTemplateProviderId("example"),
            "Example template",
            order: 30);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(codecDescriptor.Order, Is.EqualTo(20));
            Assert.That(codec.FileExtensions, Is.EqualTo(new[] { ".example" }));
            Assert.That(template.Name, Is.EqualTo("Example template"));
        }
    }

    [Test]
    public void CustomFixedAvailabilityOperation_PreservesTheOpenOperationAndModelIds()
    {
        var operation = new AiOperationId("vendor.custom");
        var model = new AiModelId("vendor-model");
        var request = new AiOperationAvailabilityRequest.Fixed(operation, model);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Operation, Is.EqualTo(operation));
            Assert.That(request.Model, Is.EqualTo(model));
            Assert.Throws<ArgumentException>(() =>
                new AiOperationAvailabilityRequest.Fixed(default));
        }
    }

    [Test]
    public void CustomVariableAvailabilityOperations_PreserveTheOpenOperationIdsAndQuantities()
    {
        var model = new AiModelId("vendor-model");
        var video = new AiOperationAvailabilityRequest.Video(
            new AiOperationId("vendor.video"),
            4,
            model);
        var transcription = new AiOperationAvailabilityRequest.Transcription(
            new AiOperationId("vendor.transcription"),
            1.5,
            model);
        var translation = new AiOperationAvailabilityRequest.Translation(
            new AiOperationId("vendor.translation"),
            123,
            model);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(video.Operation.Value, Is.EqualTo("vendor.video"));
            Assert.That(video.DurationSeconds, Is.EqualTo(4));
            Assert.That(video.Model, Is.EqualTo(model));
            Assert.That(transcription.Operation.Value, Is.EqualTo("vendor.transcription"));
            Assert.That(transcription.DurationSeconds, Is.EqualTo(1.5));
            Assert.That(transcription.Model, Is.EqualTo(model));
            Assert.That(translation.Operation.Value, Is.EqualTo("vendor.translation"));
            Assert.That(translation.CharacterCount, Is.EqualTo(123));
            Assert.That(translation.Model, Is.EqualTo(model));
        }
    }

    [Test]
    public void CustomOperations_SelectSupportedCapabilitySchemas()
    {
        var operation = new AiOperationId("vendor.video");
        var imageOperation = new AiOperationId("vendor.image");
        var catalog = new AiModelCatalog(
            [],
            capabilitySchemas:
            [
                KeyValuePair.Create(operation, AiModelCapabilitySchema.Video),
                KeyValuePair.Create(imageOperation, AiModelCapabilitySchema.Image),
            ],
            imageReferenceLimits:
            [
                KeyValuePair.Create(imageOperation, new AiImageReferenceLimits(12 * 1024 * 1024)),
            ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.GetCapabilitySchema(operation),
                Is.EqualTo(AiModelCapabilitySchema.Video));
            Assert.That(catalog.GetCapabilitySchema(new AiOperationId("vendor.generic")),
                Is.EqualTo(AiModelCapabilitySchema.Generic));
            Assert.That(catalog.GetImageReferenceLimits(imageOperation).MaxTotalBytes,
                Is.EqualTo(12 * 1024 * 1024));
            Assert.That(
                Enum.GetValues<AiModelCapabilitySchema>(),
                Is.EqualTo(new[]
                {
                    AiModelCapabilitySchema.Generic,
                    AiModelCapabilitySchema.Image,
                    AiModelCapabilitySchema.Video,
                }));
            Assert.That(typeof(AiModelCatalog).Assembly.GetType(
                "Beutl.Api.Services.AiModelCapabilitySchemaId"), Is.Null);
        }
    }

    [Test]
    public void CaptionTemplates_ComposeWithoutExposingSourcesToPlacement()
    {
        var factory = new CustomCaptionFactory();
        var template = new CaptionTemplate(
            new CaptionTemplateDescriptor(
                new CaptionTemplateId("vendor.caption"),
                new CaptionTemplateProviderId("vendor"),
                "Vendor caption"),
            factory,
            new CustomCaptionPlacement());

        ElementDescription result = template.CreateElements(
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text"),
            new CaptionElementContext(2, "Caption")).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Source, Is.SameAs(factory.Source));
            Assert.That(result.Layer, Is.EqualTo(8));
            Assert.That(result.Position, Is.EqualTo(new Point(10, 20)));
            Assert.That(
                typeof(CaptionElementPlacement).GetProperties()
                    .Select(property => property.PropertyType),
                Does.Not.Contain(typeof(ElementSource)));
            Assert.That(typeof(CaptionTemplateRegistry).Assembly.GetType(
                "Beutl.Editor.Services.Captions.CaptionTemplateContribution"), Is.Null);
            Assert.That(typeof(CaptionTemplateRegistry).Assembly.GetType(
                "Beutl.Editor.Services.Captions.CaptionTemplateRegistration"), Is.Null);
            Assert.That(typeof(CaptionTemplateRegistry).Assembly.GetType(
                "Beutl.Editor.Services.Captions.CaptionTemplateExtension"), Is.Null);
        }
    }

    private sealed class MetadataExtensionProvider(ExtensionDescriptor descriptor) : IExtensionProvider
    {
        public event EventHandler? ExtensionsChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<ExtensionDescriptor> Extensions { get; } = [descriptor];

        public IReadOnlyList<ExtensionDescriptor> GetDescriptors<TExtension>()
            where TExtension : Extension
            => Extensions;

        public bool TryAcquire<TExtension>(
            ExtensionId id,
            [NotNullWhen(true)] out IExtensionLease<TExtension>? lease)
            where TExtension : Extension
        {
            lease = null;
            return false;
        }
    }

    private sealed class CustomCaptionFactory : ICaptionElementFactory
    {
        public ElementSource Source { get; } =
            new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.TextBlock());

        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            => [new ElementDescription(MediaTime.FromTimeSpan(cue.Start), MediaTime.FromTimeSpan(cue.End - cue.Start), context.Layer, Source)];
    }

    private sealed class CustomCaptionPlacement : ICaptionPlacementPolicy
    {
        public CaptionElementPlacement Place(
            CaptionCue cue,
            CaptionElementContext context,
            CaptionElementPlacement placement,
            int elementIndex)
            => placement with { Layer = 8, Position = new Point(10, 20) };
    }
}
