using System.Diagnostics.CodeAnalysis;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
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
            Assert.That(provider.GetProperty(nameof(IExtensionProvider.Extensions)), Is.Not.Null);
            Assert.That(provider.GetEvent(nameof(IExtensionProvider.ExtensionsChanged)), Is.Not.Null);
            Assert.That(provider.GetProperty("AllExtensions"), Is.Null);
            Assert.That(provider.GetMethod("GetExtensions"), Is.Null);
            Assert.That(provider.GetMethod("MatchEditorExtension"), Is.Null);
            Assert.That(typeof(ExtensionProvider).GetProperty("AllExtensions"), Is.Null);
            Assert.That(typeof(ExtensionProvider).GetMethod("GetExtensions"), Is.Null);
            Assert.That(
                typeof(ExtensionDescriptor).GetProperties().Select(property => property.PropertyType),
                Does.Not.Contain(typeof(Type)).And.Not.Contain(typeof(Extension)));
            Assert.That(
                typeof(ElementSourceHandlerDescriptor).GetProperties()
                    .Select(property => property.PropertyType),
                Does.Not.Contain(typeof(Type)).And.Not.Contain(typeof(IElementSourceHandler)));
        }
    }

    [Test]
    public async Task CaptionCatalogSurface_DoesNotExposeMutableRegistries()
    {
        await using CaptionCatalog catalog = CaptionCatalog.CreateDefault("Default");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Codecs, Is.InstanceOf<ICaptionCodecProvider>());
            Assert.That(catalog.Codecs, Is.Not.InstanceOf<CaptionCodecRegistry>());
            Assert.That(catalog.Templates, Is.InstanceOf<ICaptionTemplateProvider>());
            Assert.That(catalog.Templates, Is.Not.InstanceOf<CaptionTemplateRegistry>());
            Assert.That(typeof(CaptionDocumentSerializer).GetProperty("Codecs"), Is.Null);
        }
    }

    [Test]
    public async Task CaptionCodecCapabilities_CanBeRegisteredIndependently()
    {
        var format = new CaptionFormatId("vendor.caption");
        var codec = new CustomCaptionCodec();
        var descriptorRegistration = new CaptionCodecDescriptorRegistration(
            new CaptionCodecDescriptor(format, [".vendor-caption"]));
        var decoderRegistration = new CaptionDecoderRegistration(format, codec);
        var encoderRegistration = new CaptionEncoderRegistration(format, codec);
        var descriptorExtension = new CustomCaptionCodecDescriptorExtension(
            descriptorRegistration);
        var decoderExtension = new CustomCaptionDecoderExtension(decoderRegistration);
        var encoderExtension = new CustomCaptionEncoderExtension(encoderRegistration);
        await using var registry = new CaptionCodecRegistry();
        await using ICaptionCodecDescriptorRegistration descriptor = registry.Register(
            descriptorRegistration);
        await using ICaptionDecoderRegistration decoder = registry.Register(
            decoderRegistration);
        await using ICaptionEncoderRegistration encoder = registry.Register(
            encoderRegistration);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Codecs.Single().CanDecode, Is.True);
            Assert.That(registry.Codecs.Single().CanEncode, Is.True);
            Assert.That(descriptorExtension.Registrations.Single(),
                Is.SameAs(descriptorRegistration));
            Assert.That(decoderExtension.Registrations.Single(),
                Is.SameAs(decoderRegistration));
            Assert.That(encoderExtension.Registrations.Single(),
                Is.SameAs(encoderRegistration));
            Assert.That(
                Enum.GetValues<CaptionCodecRegistrationMode>(),
                Is.EqualTo(new[]
                {
                    CaptionCodecRegistrationMode.Add,
                    CaptionCodecRegistrationMode.Replace,
                }));
            Assert.That(typeof(CaptionCodecRegistry).Assembly.GetType(
                "Beutl.Editor.Services.Captions.CaptionCodecContribution"), Is.Null);
            Assert.That(typeof(CaptionCodecRegistry).Assembly.GetType(
                "Beutl.Editor.Services.Captions.CaptionCodecRegistration"), Is.Null);
            Assert.That(typeof(CaptionCodecRegistry).Assembly.GetType(
                "Beutl.Editor.Services.Captions.CaptionCodecExtension"), Is.Null);
        }
    }

    [Test]
    public async Task CustomElementSource_CanDelegateLengthToItsHandler()
    {
        var handler = new CustomSourceHandler();
        await using var registry = new ElementSourceHandlerRegistry();
        await using IElementSourceHandlerRegistration registration = registry.Register(
            new ElementSourceHandlerRegistration(handler));
        var description = new ElementDescription(
            TimeSpan.Zero,
            null,
            0,
            new CustomSource());

        Assert.That(description.Length, Is.Null);
        Assert.That(registry.Handlers, Is.SameAs(registry.Handlers));
        Assert.That(registry.Handlers.Single().SourceTypeName, Is.EqualTo(typeof(CustomSource).AssemblyQualifiedName));
        Assert.That(registry.TryAcquire(typeof(CustomSource), out IElementSourceHandlerLease? lease), Is.True);
        using (lease)
        {
            Assert.That(lease!.Handler, Is.SameAs(handler));
        }
    }

    [Test]
    public void MetadataTypes_CanBeCreatedByExternalProviderImplementations()
    {
        var extension = new ExtensionDescriptor(
            new ExtensionId(Guid.NewGuid()),
            "Example.Extension, Example.Package");
        IExtensionProvider provider = new MetadataExtensionProvider(extension);
        var handler = new ElementSourceHandlerDescriptor(
            "Example.Source, Example.Package",
            order: 10);
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
            Assert.That(provider.Extensions.Single(), Is.SameAs(extension));
            Assert.That(handler.SourceTypeName, Is.EqualTo("Example.Source, Example.Package"));
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
    public async Task AiJobResultCapabilities_CanBeRegisteredAndResolvedIndependently()
    {
        var kind = new AiJobKindId("vendor.result");
        var capabilities = new CustomAiJobResultCapabilities();
        await using var registry = new AiJobResultRegistry(
            [new AiJobPresentationRegistration(kind, capabilities)],
            [new AiJobCompletionRegistration(kind, capabilities)],
            [new AiJobResultApplicatorRegistration(kind, capabilities)]);

        Assert.That(registry.TryAcquirePresenter(kind, out IAiJobPresenterLease? presenter), Is.True);
        using (presenter)
        {
            Assert.That(presenter!.Presenter, Is.SameAs(capabilities));
        }

        Assert.That(registry.TryAcquireCompletionPresenter(
            kind,
            out IAiJobCompletionPresenterLease? completion), Is.True);
        using (completion)
        {
            Assert.That(completion!.Presenter, Is.SameAs(capabilities));
        }

        Assert.That(registry.TryAcquireApplicator(
            kind,
            out IAiJobResultApplicatorLease? applicator), Is.True);
        using (applicator)
        {
            Assert.That(applicator!.Applicator, Is.SameAs(capabilities));
        }

        Assert.That(
            typeof(AiJobResultRegistry).Assembly.GetType(
                "Beutl.Editor.Services.AI.IAiJobResultHandler"),
            Is.Null);
    }

    [Test]
    public void CustomOperations_SelectSupportedCapabilitySchemas()
    {
        var operation = new AiOperationId("vendor.video");
        var imageOperation = new AiOperationId("vendor.image");
        var registration = new AiOperationCapabilitySchemaRegistration(
            operation,
            AiModelCapabilitySchema.Video);
        var extension = new CustomCapabilitySchemaExtension(registration);
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
            Assert.That(extension.Registrations.Single(), Is.SameAs(registration));
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
    public async Task AiJobBehaviors_CanBeRegisteredAndLeasedIndependently()
    {
        var kind = new AiJobKindId("vendor.job");
        var behavior = new CustomAiJobBehavior();
        await using var registry = new AiJobKindRegistry(
            [new AiJobStatusResolverRegistration(kind, behavior)],
            [new AiJobRefreshHandlerRegistration(kind, behavior)],
            [new AiJobRetryHandlerRegistration(kind, behavior)]);

        Assert.That(registry.TryAcquireStatusResolver(
            kind,
            out IAiJobStatusResolverLease? status), Is.True);
        Assert.That(registry.TryAcquireRefreshHandler(
            kind,
            out IAiJobRefreshHandlerLease? refresh), Is.True);
        Assert.That(registry.TryAcquireRetryHandler(
            kind,
            out IAiJobRetryHandlerLease? retry), Is.True);
        using (status)
        using (refresh)
        using (retry)
        using (Assert.EnterMultipleScope())
        {
            Assert.That(status!.Resolver, Is.SameAs(behavior));
            Assert.That(refresh!.Handler, Is.SameAs(behavior));
            Assert.That(retry!.Handler, Is.SameAs(behavior));
            Assert.That(typeof(AiJobKindRegistry).Assembly.GetType(
                "Beutl.Api.Services.AiJobKindDescriptor"), Is.Null);
            Assert.That(typeof(AiJobKindRegistry).Assembly.GetType(
                "Beutl.Api.Services.AiJobKindExtension"), Is.Null);
            Assert.That(typeof(AiJobKindRegistry).Assembly.GetType(
                "Beutl.Api.Services.AiJobKindRegistrationMode"), Is.Null);
            Assert.That(typeof(AiJobKindRegistry).Assembly.GetType(
                "Beutl.Api.Services.IAiJobKindRegistration"), Is.Null);
            Assert.That(typeof(AiJobKindRegistry).Assembly.GetType(
                "Beutl.Api.Services.IAiJobKindLease"), Is.Null);
        }
    }

    [Test]
    public async Task CaptionTemplateCapabilities_ComposeWithoutExposingSourcesToPlacement()
    {
        var id = new CaptionTemplateId("vendor.caption");
        var factory = new CustomCaptionFactory();
        var placement = new CustomCaptionPlacement();
        var registrations = new CaptionTemplateRegistrationSet(
            new CaptionTemplateDescriptorRegistration(new CaptionTemplateDescriptor(
                id,
                new CaptionTemplateProviderId("vendor"),
                "Vendor caption")),
            new CaptionElementFactoryRegistration(id, factory),
            new CaptionPlacementPolicyRegistration(id, placement));
        await using var registry = new CaptionTemplateRegistry(
            [registrations.DescriptorRegistration],
            [registrations.ElementFactoryRegistration],
            [registrations.PlacementPolicyRegistration]);
        using CaptionTemplateLease lease = registry.Acquire(id);

        ElementDescription result = lease.CreateElements(
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

    private sealed record CustomSource : ElementSource;

    private sealed class CustomCapabilitySchemaExtension(
        params AiOperationCapabilitySchemaRegistration[] registrations)
        : AiOperationCapabilitySchemaExtension
    {
        public override IReadOnlyCollection<AiOperationCapabilitySchemaRegistration> Registrations
        { get; } = registrations;
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

    private sealed class CustomSourceHandler : IElementSourceHandler
    {
        public Type SourceType => typeof(CustomSource);

        public ValueTask<ElementSourcePreflightResult> PreflightAsync(
            ElementSourcePreflightContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public ValueTask<ElementSourceMaterializationResult> MaterializeAsync(
            ElementSourceMaterializationContext context,
            IElementSourcePreflight preflight,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class CustomAiJobResultCapabilities :
        IAiJobPresenter,
        IAiJobCompletionPresenter,
        IAiJobResultApplicator
    {
        public AiJobPresentation Present(AiJob job, AiJobStatusSemantics status)
            => new("Vendor", "Ready", "Result", string.Empty, false);

        public AiJobCompletionPresentation? CreateCompletion(
            AiJob job,
            AiJobStatusSemantics status)
            => null;

        public bool CanApply(AiJob job, AiJobStatusSemantics status) => true;

        public Task ApplyAsync(
            AiJob job,
            IAiJobResultContext context,
            CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class CustomAiJobBehavior :
        IAiJobStatusResolver,
        IAiJobRefreshHandler,
        IAiJobRetryHandler
    {
        public AiJobStatusSemantics Resolve(AiJobStatusId status)
            => AiJobStatusSemantics.Unknown;

        public Task RefreshAsync(AiJob job, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public bool CanRetry(AiJob job, AiJobStatusSemantics status) => false;

        public ValueTask<AiJobRetryPreflight> GetPreflightAsync(
            AiJob job,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new AiJobRetryPreflight(false, false, "unavailable"));

        public ValueTask<AiJobRetryPreparationResult> PrepareAsync(
            AiJob job,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(AiJobRetryPreparationResult.Blocked("unavailable"));
    }

    private sealed class CustomCaptionFactory : ICaptionElementFactory
    {
        public ElementSource Source { get; } =
            new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.TextBlock());

        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            => [new ElementDescription(cue.Start, cue.End - cue.Start, context.Layer, Source)];
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

    private sealed class CustomCaptionCodec : ICaptionDecoder, ICaptionEncoder
    {
        public CaptionImportResult Decode(string content)
            => CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), content),
            ]));

        public string Encode(CaptionDocument document) => document[0].Text;
    }

    private sealed class CustomCaptionCodecDescriptorExtension(
        params CaptionCodecDescriptorRegistration[] registrations)
        : CaptionCodecDescriptorExtension
    {
        public override IReadOnlyCollection<CaptionCodecDescriptorRegistration> Registrations
        { get; } = registrations;
    }

    private sealed class CustomCaptionDecoderExtension(
        params CaptionDecoderRegistration[] registrations)
        : CaptionDecoderExtension
    {
        public override IReadOnlyCollection<CaptionDecoderRegistration> Registrations
        { get; } = registrations;
    }

    private sealed class CustomCaptionEncoderExtension(
        params CaptionEncoderRegistration[] registrations)
        : CaptionEncoderExtension
    {
        public override IReadOnlyCollection<CaptionEncoderRegistration> Registrations
        { get; } = registrations;
    }
}
