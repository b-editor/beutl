namespace Beutl.UnitTests.Editor.Services.Captions;

using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Beutl.Api.Services;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Extensibility;
using Beutl.Graphics;

[TestFixture]
public sealed class CaptionTemplateRegistryTests
{
    private static readonly CaptionTemplateProviderId s_provider = new("beutl.tests");

    [Test]
    public async Task Register_RejectsCaseInsensitiveIdentifierCollision()
    {
        await using CaptionTemplateRegistry registry = CreateRegistry(
            CreateText("Vendor.Template", "First"));

        Assert.That(() => registry.Register(
                CreateText("vendor.template", "Second").DescriptorRegistration),
            Throws.ArgumentException.With.Message.Contains("already has a descriptor"));
    }

    [Test]
    public async Task Register_RequiresExplicitReplacementOfExistingTemplate()
    {
        CaptionTemplateRegistrationSet original = CaptionTemplateDefaults.CreateDefaultText("Default");
        CaptionTemplateRegistrationSet replacement = CaptionTemplateDefaults.CreateText(
            CaptionTemplateIds.DefaultText,
            s_provider,
            "Replacement");
        await using CaptionTemplateRegistry registry = CreateRegistry(original);

        await using ICaptionTemplateDescriptorRegistration replacementRegistration = registry.Register(
            new CaptionTemplateDescriptorRegistration(
                replacement.DescriptorRegistration.Descriptor,
                CaptionTemplateRegistrationMode.Replace));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                registry.GetRequired(CaptionTemplateIds.DefaultText).Name,
                Is.EqualTo("Replacement"));
            Assert.That(
                () => registry.Register(new CaptionTemplateDescriptorRegistration(
                    CreateText("beutl.tests.missing", "Missing").DescriptorRegistration.Descriptor,
                    CaptionTemplateRegistrationMode.Replace)),
                Throws.ArgumentException.With.Message.Contains("no descriptor"));
        }
    }

    [Test]
    public async Task DescriptorFactoryAndPlacementReplaceAndRestoreIndependently()
    {
        CaptionTemplateRegistrationSet original = CreateText(
            "beutl.tests.independent-template",
            "Original");
        CaptionTemplateId id = original.DescriptorRegistration.Descriptor.Id;
        var replacementFactory = new TaggedFactory("Replacement factory");
        var replacementPlacement = new FixedPlacementPolicy(new Point(9, 10), layer: 42);
        await using CaptionTemplateRegistry registry = CreateRegistry(original);
        ICaptionTemplateDescriptorRegistration descriptor = registry.Register(
            new CaptionTemplateDescriptorRegistration(
                new CaptionTemplateDescriptor(id, s_provider, "Replacement"),
                CaptionTemplateRegistrationMode.Replace));
        ICaptionElementFactoryRegistration factory = registry.Register(
            new CaptionElementFactoryRegistration(
                id,
                replacementFactory,
                CaptionTemplateRegistrationMode.Replace));
        ICaptionPlacementPolicyRegistration placement = registry.Register(
            new CaptionPlacementPolicyRegistration(
                id,
                replacementPlacement,
                CaptionTemplateRegistrationMode.Replace));
        var cue = new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text");
        var context = new CaptionElementContext(3, "Caption", new Point(1, 2));
        try
        {
            Assert.That(registry.GetRequired(id).Name, Is.EqualTo("Replacement"));
            ElementDescription replaced = CreateOne(registry, id, cue, context);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(replaced.Name, Is.EqualTo("Replacement factory"));
                Assert.That(replaced.Position, Is.EqualTo(new Point(9, 10)));
                Assert.That(replaced.Layer, Is.EqualTo(42));
            }

            await descriptor.DisposeAsync();
            Assert.That(registry.GetRequired(id).Name, Is.EqualTo("Original"));
            Assert.That(CreateOne(registry, id, cue, context).Name,
                Is.EqualTo("Replacement factory"));

            await factory.DisposeAsync();
            ElementDescription originalFactory = CreateOne(registry, id, cue, context);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(originalFactory.Name, Is.EqualTo("Caption"));
                Assert.That(originalFactory.Position, Is.EqualTo(new Point(9, 10)));
                Assert.That(originalFactory.Layer, Is.EqualTo(42));
            }

            await placement.DisposeAsync();
            ElementDescription restored = CreateOne(registry, id, cue, context);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(restored.Layer, Is.EqualTo(3));
                Assert.That(restored.Position, Is.EqualTo(new Point(1, 2)));
            }
        }
        finally
        {
            await descriptor.DisposeAsync();
            await factory.DisposeAsync();
            await placement.DisposeAsync();
        }
    }

    [Test]
    public async Task PartialTemplateIsPublishedOnlyAfterEverySlotExists()
    {
        CaptionTemplateRegistrationSet template = CreateText(
            "beutl.tests.partial-template",
            "Partial");
        await using var registry = new CaptionTemplateRegistry();
        await using ICaptionTemplateDescriptorRegistration descriptor =
            registry.Register(template.DescriptorRegistration);
        Assert.That(registry.Templates, Is.Empty);
        await using ICaptionElementFactoryRegistration factory =
            registry.Register(template.ElementFactoryRegistration);
        Assert.That(registry.Templates, Is.Empty);
        ICaptionPlacementPolicyRegistration placement = registry.Register(template.PlacementPolicyRegistration);

        Assert.That(registry.Templates.Select(item => item.Name), Is.EqualTo(["Partial"]));

        await placement.DisposeAsync();
        Assert.That(registry.Templates, Is.Empty);
    }

    [Test]
    public async Task DisposingDescriptorBaseKeepsItsActiveReplacementUntilThatHandleEnds()
    {
        var id = new CaptionTemplateId("beutl.tests.base-disposal");
        CaptionTemplateRegistrationSet template = CreateTemplate(
            id,
            "Base",
            new TaggedFactory("Factory"),
            PreserveCaptionPlacementPolicy.Instance);
        await using var registry = new CaptionTemplateRegistry();
        ICaptionTemplateDescriptorRegistration baseRegistration = registry.Register(
            template.DescriptorRegistration);
        await using ICaptionElementFactoryRegistration factory = registry.Register(
            template.ElementFactoryRegistration);
        await using ICaptionPlacementPolicyRegistration placement = registry.Register(
            template.PlacementPolicyRegistration);
        ICaptionTemplateDescriptorRegistration replacement = registry.Register(
            new CaptionTemplateDescriptorRegistration(
                new CaptionTemplateDescriptor(id, s_provider, "Replacement"),
                CaptionTemplateRegistrationMode.Replace));
        try
        {
            await baseRegistration.DisposeAsync();
            Assert.That(registry.GetRequired(id).Name, Is.EqualTo("Replacement"));

            await replacement.DisposeAsync();
            Assert.That(registry.TryGet(id, out _), Is.False);
        }
        finally
        {
            await baseRegistration.DisposeAsync();
            await replacement.DisposeAsync();
        }
    }

    [Test]
    public async Task ReplacedFactoryRegistrationWaitsForItsRetiredTemplateLease()
    {
        CaptionTemplateRegistrationSet template = CreateText(
            "beutl.tests.direct-owner-drain",
            "Direct owner drain");
        CaptionTemplateId id = template.DescriptorRegistration.Descriptor.Id;
        await using var registry = new CaptionTemplateRegistry();
        await using ICaptionTemplateDescriptorRegistration descriptor = registry.Register(
            template.DescriptorRegistration);
        ICaptionElementFactoryRegistration factory = registry.Register(template.ElementFactoryRegistration);
        await using ICaptionPlacementPolicyRegistration placement = registry.Register(
            template.PlacementPolicyRegistration);
        using CaptionTemplateLease oldLease = registry.Acquire(id);
        await using ICaptionElementFactoryRegistration replacement = registry.Register(
            new CaptionElementFactoryRegistration(
                id,
                new TaggedFactory("Replacement"),
                CaptionTemplateRegistrationMode.Replace));

        Task disposal = factory.DisposeAsync().AsTask();
        await Task.Yield();
        Assert.That(disposal.IsCompleted, Is.False);

        oldLease.Dispose();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task EqualReplacementEntriesKeepReferenceIdentityForOwnerDrains()
    {
        CaptionTemplateRegistrationSet template = CreateText(
            "beutl.tests.equal-registration-owners",
            "Equal registration owners");
        CaptionTemplateId id = template.DescriptorRegistration.Descriptor.Id;
        await using var registry = new CaptionTemplateRegistry();
        await using ICaptionTemplateDescriptorRegistration descriptor = registry.Register(
            template.DescriptorRegistration);
        await using ICaptionElementFactoryRegistration factory = registry.Register(
            template.ElementFactoryRegistration);
        await using ICaptionPlacementPolicyRegistration placement = registry.Register(
            template.PlacementPolicyRegistration);
        var replacementRegistration = new CaptionElementFactoryRegistration(
            id,
            new TaggedFactory("Replacement"),
            CaptionTemplateRegistrationMode.Replace);
        ICaptionElementFactoryRegistration firstReplacement = registry.Register(
            replacementRegistration);
        ICaptionElementFactoryRegistration secondReplacement = registry.Register(
            replacementRegistration);
        try
        {
            using (CaptionTemplateLease topLease = registry.Acquire(id))
            {
                Task topDisposal = secondReplacement.DisposeAsync().AsTask();
                await Task.Yield();
                Assert.That(topDisposal.IsCompleted, Is.False);
                topLease.Dispose();
                await topDisposal.WaitAsync(TimeSpan.FromSeconds(5));
            }

            using CaptionTemplateLease fallbackLease = registry.Acquire(id);
            Task fallbackDisposal = firstReplacement.DisposeAsync().AsTask();
            await Task.Yield();
            Assert.That(fallbackDisposal.IsCompleted, Is.False);
            fallbackLease.Dispose();
            await fallbackDisposal.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await secondReplacement.DisposeAsync();
            await firstReplacement.DisposeAsync();
        }
    }

    [Test]
    public async Task RegistryDisposeWaitsForLeaseFromStateRetiredByReplacement()
    {
        CaptionTemplateRegistrationSet template = CreateText(
            "beutl.tests.registry-retired-state-drain",
            "Registry retired-state drain");
        CaptionTemplateId id = template.DescriptorRegistration.Descriptor.Id;
        var registry = new CaptionTemplateRegistry();
        await using ICaptionTemplateDescriptorRegistration descriptor = registry.Register(
            template.DescriptorRegistration);
        await using ICaptionElementFactoryRegistration factory = registry.Register(
            template.ElementFactoryRegistration);
        await using ICaptionPlacementPolicyRegistration placement = registry.Register(
            template.PlacementPolicyRegistration);
        using CaptionTemplateLease oldLease = registry.Acquire(id);
        await using ICaptionElementFactoryRegistration replacement = registry.Register(
            new CaptionElementFactoryRegistration(
                id,
                new TaggedFactory("Replacement"),
                CaptionTemplateRegistrationMode.Replace));

        Task disposal = registry.DisposeAsync().AsTask();
        await Task.Yield();
        Assert.That(disposal.IsCompleted, Is.False);

        oldLease.Dispose();
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task DataOnlyPlacementCannotReplaceTheFactoryCreatedSource()
    {
        var source = new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.TextBlock());
        var factory = new FixedSourceFactory(source);
        var template = CreateTemplate(
            new CaptionTemplateId("beutl.tests.source-preservation"),
            "Source preservation",
            factory,
            new FixedPlacementPolicy(new Point(12, 34), layer: 9));
        await using CaptionTemplateRegistry registry = CreateRegistry(template);

        ElementDescription description = CreateOne(
            registry,
            template.DescriptorRegistration.Descriptor.Id,
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text"),
            new CaptionElementContext(0, "Caption"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(description.Source, Is.SameAs(source));
            Assert.That(description.Position, Is.EqualTo(new Point(12, 34)));
            Assert.That(description.Layer, Is.EqualTo(9));
            Assert.That(
                typeof(CaptionElementPlacement).GetProperties()
                    .Select(property => property.PropertyType),
                Does.Not.Contain(typeof(ElementSource)));
        }
    }

    [Test]
    public async Task Templates_AreOrderedByExplicitOrderThenStableIdentifier()
    {
        await using CaptionTemplateRegistry registry = CreateRegistry(
            CreateText("beutl.tests.zulu", "Zulu", order: 10),
            CreateText("beutl.tests.beta", "Beta"),
            CreateText("beutl.tests.alpha", "Alpha"));

        Assert.That(
            registry.Templates.Select(template => template.Name),
            Is.EqualTo(new[] { "Alpha", "Beta", "Zulu" }));
    }

    [Test]
    public async Task Register_MetadataObserverFailureDoesNotInterruptRegistryReplacement()
    {
        await using var registry = new CaptionTemplateRegistry();
        CaptionTemplateRegistrationSet template = CreateText("beutl.tests.safe", "Safe");
        await using ICaptionElementFactoryRegistration factory = registry.Register(
            template.ElementFactoryRegistration);
        await using ICaptionPlacementPolicyRegistration placement = registry.Register(
            template.PlacementPolicyRegistration);
        registry.Templates.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("Observer failure");

        Assert.DoesNotThrow(() => registry.Register(template.DescriptorRegistration));
        Assert.That(registry.GetRequired(new CaptionTemplateId("beutl.tests.safe")).Name, Is.EqualTo("Safe"));
    }

    [Test]
    public async Task Catalog_ComposesCodecAndTemplateExtensionsForNonUiConsumers()
    {
        var format = new CaptionFormatId("beutl.tests.caption");
        CaptionTemplateRegistrationSet template = CreateText(
            "beutl.tests.catalog-template",
            "Catalog template");
        var codec = new TestCodec();

        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions);
        extensions.AddExtensions(1,
        [
            .. CreateCodecExtensions(format, codec),
            .. CreateTemplateExtensions(template),
        ]);
        try
        {
            CaptionImportResult imported = catalog.Serializer.Import(
                "From extension"u8,
                format);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(imported.IsSuccess, Is.True);
                Assert.That(imported.Document!.Cues.Single().Text, Is.EqualTo("From extension"));
                Assert.That(
                    catalog.Templates.GetRequired(template.DescriptorRegistration.Descriptor.Id).Name,
                    Is.EqualTo(template.DescriptorRegistration.Descriptor.Name));
            }
        }
        finally
        {
            extensions.RemoveExtensions(1);
        }
    }

    [Test]
    public async Task Catalog_CodecMetadataListStaysStableAndNotifiesOnExtensionChanges()
    {
        var format = new CaptionFormatId("beutl.tests.observable-codec");
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        var metadata = catalog.Codecs.Codecs;
        int changes = 0;
        metadata.CollectionChanged += (_, _) => changes++;

        extensions.AddExtensions(2,
        [
            new RegistrationCodecDescriptorExtension([
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(format, [".observable-caption"])),
            ]),
        ]);
        Assert.That(catalog.Codecs.Codecs, Is.SameAs(metadata));
        Assert.That(catalog.Codecs.TryGet(format, out _), Is.True);

        await extensions.RemoveExtensions(2).DrainAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Codecs.Codecs, Is.SameAs(metadata));
            Assert.That(catalog.Codecs.TryGet(format, out _), Is.False);
            Assert.That(changes, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Catalog_DoesNotPublishACodecFromARolledBackExtensionBatch()
    {
        var format = new CaptionFormatId("beutl.tests.rolled-back-codec");
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        int metadataChanges = 0;
        catalog.Codecs.Codecs.CollectionChanged += (_, _) => metadataChanges++;
        var observerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseObserver = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        NotifyCollectionChangedEventHandler failingObserver = (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add)
                return;
            observerEntered.TrySetResult();
            releaseObserver.Task.GetAwaiter().GetResult();
            throw new InvalidOperationException("Injected extension observer failure.");
        };
        extensions.AllExtensions.CollectionChanged += failingObserver;
        Task<Exception?> addition = Task.Run(() =>
        {
            try
            {
                extensions.AddExtensions(3,
                [
                    new RegistrationCodecDescriptorExtension([
                        new CaptionCodecDescriptorRegistration(
                            new CaptionCodecDescriptor(format, [".rolled-back-caption"])),
                    ]),
                ]);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        try
        {
            await observerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(catalog.Codecs.TryGet(format, out _), Is.False);
            releaseObserver.TrySetResult();
            Exception? failure = await addition.WaitAsync(TimeSpan.FromSeconds(5));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(failure, Is.TypeOf<ExtensionRegistrationNotificationException>());
                Assert.That(catalog.Codecs.TryGet(format, out _), Is.False);
                Assert.That(metadataChanges, Is.Zero);
            }
        }
        finally
        {
            releaseObserver.TrySetResult();
            extensions.AllExtensions.CollectionChanged -= failingObserver;
            await addition.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task Catalog_ComposesCodecReplacementEnumeratedBeforeItsBase()
    {
        var format = new CaptionFormatId("beutl.tests.dependent-caption");
        var replacementCodec = new PrefixDecoder("replacement");
        var baseCodec = new PrefixDecoder("base");
        var replacement = new RegistrationDecoderExtension(
        [
            new CaptionDecoderRegistration(
                format,
                replacementCodec,
                CaptionCodecRegistrationMode.Replace),
        ]);
        var @base = new RegistrationDecoderExtension(
        [
            new CaptionDecoderRegistration(format, baseCodec),
        ]);
        var descriptor = new RegistrationCodecDescriptorExtension(
        [
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(format, [".dependent-caption"])),
        ]);
        var failures = new List<CaptionCatalogExtensionFailure>();
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions,
            failures.Add);

        extensions.AddExtensions(5, [replacement]);
        Assert.That(failures, Has.Count.EqualTo(1));
        failures.Clear();
        extensions.AddExtensions(6, [@base, descriptor]);

        CaptionCodecInfo info = catalog.Codecs.GetRequired(format);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.CanDecode, Is.True);
            Assert.That(info.CanEncode, Is.False);
            Assert.That(
                catalog.Serializer.Import("text"u8, format).Document![0].Text,
                Is.EqualTo("replacement:text"));
            Assert.That(replacement.RegistrationsReadCount, Is.EqualTo(2));
            Assert.That(@base.RegistrationsReadCount, Is.EqualTo(1));
            Assert.That(failures, Is.Empty);
        }

        await extensions.RemoveExtensions(5).DrainAsync();
        Assert.That(
            catalog.Serializer.Import("text"u8, format).Document![0].Text,
            Is.EqualTo("base:text"));
        await extensions.RemoveExtensions(6).DrainAsync();
    }

    [Test]
    public async Task Catalog_ComposesIndependentCodecSlotsFromSeparateExtensions()
    {
        var format = new CaptionFormatId("beutl.tests.independent-slots");
        var codec = new TestCodec();
        var descriptor = new RegistrationCodecDescriptorExtension(
        [
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(format, [".independent-slots"])),
        ]);
        var decoder = new RegistrationDecoderExtension(
        [
            new CaptionDecoderRegistration(format, codec),
        ]);
        var encoder = new RegistrationEncoderExtension(
        [
            new CaptionEncoderRegistration(format, codec),
        ]);
        var failures = new List<CaptionCatalogExtensionFailure>();
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions,
            failures.Add);

        extensions.AddExtensions(6, [encoder, descriptor, decoder]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Codecs.GetRequired(format).CanDecode, Is.True);
            Assert.That(catalog.Codecs.GetRequired(format).CanEncode, Is.True);
            Assert.That(failures, Is.Empty);
        }
    }

    [Test]
    public async Task Catalog_DecoderOnlyReplacementPreservesBuiltInDescriptorAndEncoder()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        var replacement = new RegistrationDecoderExtension(
        [
            new CaptionDecoderRegistration(
                CaptionFormats.Srt,
                new PrefixDecoder("replacement"),
                CaptionCodecRegistrationMode.Replace),
        ]);

        extensions.AddExtensions(7, [replacement]);

        CaptionCodecInfo info = catalog.Codecs.GetRequired(CaptionFormats.Srt);
        byte[] exported = catalog.Serializer.Export(
            new CaptionDocument([
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text"),
            ]),
            CaptionFormats.Srt);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.FileExtensions, Is.EqualTo(new[] { ".srt" }));
            Assert.That(info.CanDecode, Is.True);
            Assert.That(info.CanEncode, Is.True);
            Assert.That(
                catalog.Serializer.Import("text"u8, CaptionFormats.Srt).Document![0].Text,
                Is.EqualTo("replacement:text"));
            Assert.That(exported, Is.Not.Empty);
        }

        await extensions.RemoveExtensions(7).DrainAsync();
        CaptionImportResult restored = catalog.Serializer.Import(
            "1\n00:00:00,000 --> 00:00:01,000\nrestored\n"u8,
            CaptionFormats.Srt);
        Assert.That(restored.Document![0].Text, Is.EqualTo("restored"));
    }

    [Test]
    public async Task Catalog_ReevaluatesHealthyCodecAfterInvalidProvisionalAddIsRejected()
    {
        var format = new CaptionFormatId("beutl.tests.provisional-codec");
        var missing = new CaptionFormatId("beutl.tests.missing-codec");
        var invalid = new RegistrationCodecDescriptorExtension(
        [
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(format, [".invalid"])),
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(missing, [".missing"]),
                CaptionCodecRegistrationMode.Replace),
        ]);
        var healthy = new RegistrationCodecDescriptorExtension(
        [
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(format, [".healthy"])),
        ]);
        var failures = new List<CaptionCatalogExtensionFailure>();
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions,
            failures.Add);

        extensions.AddExtensions(7, [invalid, healthy]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Codecs.GetRequired(format).FileExtensions,
                Is.EqualTo(new[] { ".healthy" }));
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0].Kind,
                Is.EqualTo(CaptionCatalogContributionKind.CodecDescriptor));
        }
    }

    [Test]
    public async Task Catalog_ReevaluatesHealthyTemplateAfterInvalidProvisionalAddIsRejected()
    {
        CaptionTemplateRegistrationSet invalidTemplate = CreateText(
            "beutl.tests.provisional-template",
            "Invalid provisional");
        CaptionTemplateRegistrationSet healthyTemplate = CreateText(
            "beutl.tests.provisional-template",
            "Healthy");
        CaptionTemplateRegistrationSet missingTemplate = CreateText(
            "beutl.tests.missing-template",
            "Missing");
        var invalid = new TestTemplateDescriptorExtension(
        [
            invalidTemplate.DescriptorRegistration,
            new CaptionTemplateDescriptorRegistration(
                missingTemplate.DescriptorRegistration.Descriptor,
                CaptionTemplateRegistrationMode.Replace),
        ]);
        var healthy = new TestTemplateDescriptorExtension([healthyTemplate.DescriptorRegistration]);
        var failures = new List<CaptionCatalogExtensionFailure>();
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions,
            failures.Add);

        extensions.AddExtensions(8,
        [
            invalid,
            healthy,
            new TestElementFactoryExtension([healthyTemplate.ElementFactoryRegistration]),
            new TestPlacementExtension([healthyTemplate.PlacementPolicyRegistration]),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Templates.GetRequired(healthyTemplate.DescriptorRegistration.Descriptor.Id).Name,
                Is.EqualTo("Healthy"));
            Assert.That(failures, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Catalog_DiscardsAnInvalidExtensionAtomically()
    {
        CaptionTemplateRegistrationSet valid = CreateText(
            "beutl.tests.valid-before-collision",
            "Valid before collision");
        CaptionTemplateRegistrationSet duplicateDefault = CaptionTemplateDefaults.CreateText(
            CaptionTemplateIds.DefaultText,
            s_provider,
            "Unexpected replacement");
        var extension = new TestTemplateDescriptorExtension(
        [
            valid.DescriptorRegistration,
            duplicateDefault.DescriptorRegistration,
        ]);
        var failures = new List<CaptionCatalogExtensionFailure>();

        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions,
            failures.Add);
        extensions.AddExtensions(1,
        [
            extension,
            new TestElementFactoryExtension([valid.ElementFactoryRegistration]),
            new TestPlacementExtension([valid.PlacementPolicyRegistration]),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(
                failures[0].Kind,
                Is.EqualTo(CaptionCatalogContributionKind.TemplateDescriptor));
            Assert.That(catalog.Templates.TryGet(valid.DescriptorRegistration.Descriptor.Id, out _), Is.False);
            Assert.That(
                catalog.Templates.GetRequired(CaptionTemplateIds.DefaultText).Name,
                Is.EqualTo("Default"));
        }

        extensions.RemoveExtensions(1);
    }

    [Test]
    public async Task Catalog_FailureReporterCannotInterruptRegistryChanges()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions,
            _ => throw new InvalidOperationException("Reporter failure"));
        var invalid = new TestTemplateDescriptorExtension([null!]);

        Assert.DoesNotThrow(() => extensions.AddExtensions(1, [invalid]));
        Assert.DoesNotThrow(() => extensions.RemoveExtensions(1));
        Assert.That(catalog.Templates.Templates, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Catalog_RefreshObjectTemplatesUpdatesTheSharedMetadataView()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        ObjectTemplateItem item = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Saved caption");

        catalog.RefreshObjectTemplates([item]);
        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Does.Contain("Saved caption"));

        catalog.RefreshObjectTemplates([]);
        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Does.Not.Contain("Saved caption"));
    }

    [Test]
    public async Task Catalog_ObjectTemplatesRemainAlphabeticalAcrossComposeAndRefresh()
    {
        var extensions = new ExtensionProvider();
        ObjectTemplateItem zulu = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Zulu");
        ObjectTemplateItem alpha = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Alpha");
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [zulu, alpha],
            extensions);

        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Is.EqualTo(new[] { "Default", "Alpha", "Zulu" }));

        ObjectTemplateItem beta = ObjectTemplateItem.CreateFromInstance(
            new Beutl.Graphics.Shapes.TextBlock(),
            "Beta");
        catalog.RefreshObjectTemplates([zulu, beta, alpha]);

        Assert.That(
            catalog.Templates.Templates.Select(template => template.Name),
            Is.EqualTo(new[] { "Default", "Alpha", "Beta", "Zulu" }));
    }

    [Test]
    public async Task Catalog_RemovingCodecExtensionDoesNotWaitForUnrelatedTemplateLease()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        Extension[] codecExtensions = CreateCodecExtensions(
            new CaptionFormatId("beutl.tests.independent-codec"),
            new TestCodec());
        CaptionTemplateRegistrationSet template = CreateText(
            "beutl.tests.independent-template",
            "Independent template");
        extensions.AddExtensions(1, codecExtensions);
        extensions.AddExtensions(2, CreateTemplateExtensions(template));
        using ICaptionTemplateLease lease = catalog.Templates.Acquire(
            template.DescriptorRegistration.Descriptor.Id);

        ExtensionRemoval removal = extensions.RemoveExtensions(1);
        await removal.DrainAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(catalog.Codecs.TryGet(new CaptionFormatId("beutl.tests.independent-codec"), out _), Is.False);
    }

    [Test, NonParallelizable]
    public async Task Catalog_RemovingCodecExtensionDoesNotWaitForAnotherPackageCodecLease()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        var removedFormat = new CaptionFormatId("beutl.tests.removed-codec");
        var activeFormat = new CaptionFormatId("beutl.tests.active-codec");
        WeakReference removedExtension = RegisterCollectibleCodecExtension(
            extensions,
            1,
            removedFormat);
        var blocking = new BlockingCodecExtension(activeFormat);
        extensions.AddExtensions(2, [blocking]);

        Task<CaptionImportResult> decode = Task.Run(() =>
            catalog.Serializer.Import("caption"u8, activeFormat));
        Assert.That(blocking.WaitForDecode(TimeSpan.FromSeconds(5)), Is.True);
        try
        {
            RemoveExtensionsAndDrain(extensions, 1);
            await Task.Yield();
            Assert.That(Collect(removedExtension), Is.False);
        }
        finally
        {
            blocking.ReleaseDecode();
        }

        Assert.That((await decode).IsSuccess, Is.True);
    }

    [Test, NonParallelizable]
    public async Task Catalog_RemovingDecoderExtensionDoesNotWaitForEncoderLease()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        var format = new CaptionFormatId("beutl.tests.independent-codec-directions");
        WeakReference removedDecoder = RegisterCollectibleDecoderExtension(
            extensions,
            1,
            format);
        var encoder = new BlockingEncoderExtension(format);
        extensions.AddExtensions(2, [encoder]);
        Task<string> encode = Task.Run(() => catalog.Codecs.Encode(
            format,
            new CaptionDocument([
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "caption"),
            ])));
        Assert.That(encoder.WaitForEncode(TimeSpan.FromSeconds(5)), Is.True);

        try
        {
            RemoveExtensionsAndDrain(extensions, 1);
            await Task.Yield();
            Assert.That(Collect(removedDecoder), Is.False);
        }
        finally
        {
            encoder.ReleaseEncode();
        }

        Assert.That(await encode.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo("caption"));
    }

    [Test, NonParallelizable]
    public async Task Catalog_RemovedTemplatePolicyCanCollectWhileAnotherPackageLeaseIsActive()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        var removedId = new CaptionTemplateId("beutl.tests.collectible-policy");
        WeakReference removedPolicy = RegisterCollectibleTemplateExtensions(
            extensions,
            3,
            removedId);
        CaptionTemplateRegistrationSet activeTemplate = CreateText(
            "beutl.tests.active-template",
            "Active template");
        extensions.AddExtensions(4, CreateTemplateExtensions(activeTemplate));
        using ICaptionTemplateLease activeLease = catalog.Templates.Acquire(
            activeTemplate.DescriptorRegistration.Descriptor.Id);

        RemoveExtensionsAndDrain(extensions, 3);
        await Task.Yield();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Templates.TryGet(removedId, out _), Is.False);
            Assert.That(Collect(removedPolicy), Is.False);
        }
    }

    [Test]
    public async Task Catalog_RemovingDecoderOwnerWaitsForItsActiveLease()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        var format = new CaptionFormatId("beutl.tests.merged-codec");
        var decoder = new BlockingCodecExtension(format);
        extensions.AddExtensions(1, [decoder]);
        extensions.AddExtensions(2, [new EncoderOnlyCodecExtension(format)]);

        Task<CaptionImportResult> decode = Task.Run(() =>
            catalog.Serializer.Import("caption"u8, format));
        Assert.That(decoder.WaitForDecode(TimeSpan.FromSeconds(5)), Is.True);
        ExtensionRemoval removal = extensions.RemoveExtensions(1);
        Task drain = removal.DrainAsync().AsTask();
        try
        {
            await Task.Delay(50);
            Assert.That(drain.IsCompleted, Is.False);
        }
        finally
        {
            decoder.ReleaseDecode();
        }

        await drain.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That((await decode).IsSuccess, Is.True);
    }

    [Test]
    public async Task Catalog_RepeatedDisposeAsyncReturnsTheSameIncompleteDrain()
    {
        var extensions = new ExtensionProvider();
        CaptionTemplateRegistrationSet template = CreateText(
            "beutl.tests.dispose-template",
            "Dispose template");
        var catalog = CaptionCatalog.Compose("Default", [], extensions);
        extensions.AddExtensions(1, CreateTemplateExtensions(template));
        using ICaptionTemplateLease lease = catalog.Templates.Acquire(
            template.DescriptorRegistration.Descriptor.Id);

        Task first = catalog.DisposeAsync().AsTask();
        Task second = catalog.DisposeAsync().AsTask();
        await Task.Yield();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
        }

        lease.Dispose();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static CaptionTemplateRegistrationSet CreateText(
        string id,
        string name,
        int order = 0)
        => CaptionTemplateDefaults.CreateText(
            new CaptionTemplateId(id),
            s_provider,
            name,
            order);

    private static CaptionTemplateRegistrationSet CreateTemplate(
        CaptionTemplateId id,
        string name,
        ICaptionElementFactory factory,
        ICaptionPlacementPolicy placement)
    {
        var descriptor = new CaptionTemplateDescriptor(id, s_provider, name);
        return new CaptionTemplateRegistrationSet(
            new CaptionTemplateDescriptorRegistration(descriptor),
            new CaptionElementFactoryRegistration(id, factory),
            new CaptionPlacementPolicyRegistration(id, placement));
    }

    private static ElementDescription CreateOne(
        CaptionTemplateRegistry registry,
        CaptionTemplateId id,
        CaptionCue cue,
        CaptionElementContext context)
    {
        using CaptionTemplateLease lease = registry.Acquire(id);
        return lease.CreateElements(cue, context).Single();
    }

    private static CaptionTemplateRegistry CreateRegistry(
        params CaptionTemplateRegistrationSet[] templates)
        => new(
            templates.Select(item => item.DescriptorRegistration),
            templates.Select(item => item.ElementFactoryRegistration),
            templates.Select(item => item.PlacementPolicyRegistration));

    private static Extension[] CreateTemplateExtensions(CaptionTemplateRegistrationSet template)
        =>
        [
            new TestTemplateDescriptorExtension([template.DescriptorRegistration]),
            new TestElementFactoryExtension([template.ElementFactoryRegistration]),
            new TestPlacementExtension([template.PlacementPolicyRegistration]),
        ];

    private static Extension[] CreateCodecExtensions(
        CaptionFormatId format,
        TestCodec codec)
        =>
        [
            new RegistrationCodecDescriptorExtension([
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(format, [".test-caption"])),
            ]),
            new RegistrationDecoderExtension([
                new CaptionDecoderRegistration(format, codec),
            ]),
            new RegistrationEncoderExtension([
                new CaptionEncoderRegistration(format, codec),
            ]),
        ];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterCollectibleCodecExtension(
        ExtensionProvider extensions,
        int packageId,
        CaptionFormatId format)
    {
        var extension = new RegistrationCodecDescriptorExtension(
        [
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(format, [".collectible-caption"])),
        ]);
        extensions.AddExtensions(packageId, [extension]);
        return new WeakReference(extension);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterCollectibleDecoderExtension(
        ExtensionProvider extensions,
        int packageId,
        CaptionFormatId format)
    {
        var decoder = new TestCodec();
        extensions.AddExtensions(packageId,
        [
            new RegistrationDecoderExtension([
                new CaptionDecoderRegistration(format, decoder),
            ]),
        ]);
        return new WeakReference(decoder);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterCollectibleTemplateExtensions(
        ExtensionProvider extensions,
        int packageId,
        CaptionTemplateId id)
    {
        var policy = new FixedPlacementPolicy(new Point(1, 2), layer: 3);
        CaptionTemplateRegistrationSet template = CreateTemplate(
            id,
            "Collectible template",
            new TaggedFactory("Collectible factory"),
            policy);
        extensions.AddExtensions(packageId, CreateTemplateExtensions(template));
        return new WeakReference(policy);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RemoveExtensionsAndDrain(
        ExtensionProvider extensions,
        int packageId)
    {
        ExtensionRemoval removal = extensions.RemoveExtensions(packageId);
        removal.DrainAsync().AsTask().GetAwaiter().GetResult();
    }

    private static bool Collect(WeakReference reference)
    {
        for (int index = 0; reference.IsAlive && index < 10; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return reference.IsAlive;
    }

    private sealed class TaggedFactory(string name) : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            => [context.CreateDescription(cue, () => new Beutl.Graphics.Shapes.TextBlock(), name: name)];
    }

    private sealed class FixedSourceFactory(ElementSource source) : ICaptionElementFactory
    {
        public IReadOnlyList<ElementDescription> CreateElements(
            CaptionCue cue,
            CaptionElementContext context)
            => [new ElementDescription(cue.Start, cue.End - cue.Start, context.Layer, source)];
    }

    private sealed class FixedPlacementPolicy(Point position, int layer) : ICaptionPlacementPolicy
    {
        public CaptionElementPlacement Place(
            CaptionCue cue,
            CaptionElementContext context,
            CaptionElementPlacement placement,
            int elementIndex)
            => placement with { Position = position, Layer = layer };
    }

    private sealed class RegistrationCodecDescriptorExtension(
        IReadOnlyCollection<CaptionCodecDescriptorRegistration> registrations)
        : CaptionCodecDescriptorExtension
    {
        public int RegistrationsReadCount { get; private set; }

        public override IReadOnlyCollection<CaptionCodecDescriptorRegistration> Registrations
        {
            get
            {
                RegistrationsReadCount++;
                return registrations;
            }
        }
    }

    private sealed class RegistrationDecoderExtension(
        IReadOnlyCollection<CaptionDecoderRegistration> registrations)
        : CaptionDecoderExtension
    {
        public int RegistrationsReadCount { get; private set; }

        public override IReadOnlyCollection<CaptionDecoderRegistration> Registrations
        {
            get
            {
                RegistrationsReadCount++;
                return registrations;
            }
        }
    }

    private sealed class RegistrationEncoderExtension(
        IReadOnlyCollection<CaptionEncoderRegistration> registrations)
        : CaptionEncoderExtension
    {
        public override IReadOnlyCollection<CaptionEncoderRegistration> Registrations
            => registrations;
    }

    private sealed class TestTemplateDescriptorExtension(
        IReadOnlyCollection<CaptionTemplateDescriptorRegistration> registrations)
        : CaptionTemplateDescriptorExtension
    {
        public override IReadOnlyCollection<CaptionTemplateDescriptorRegistration> Registrations
            => registrations;
    }

    private sealed class TestElementFactoryExtension(
        IReadOnlyCollection<CaptionElementFactoryRegistration> registrations)
        : CaptionElementFactoryExtension
    {
        public override IReadOnlyCollection<CaptionElementFactoryRegistration> Registrations
            => registrations;
    }

    private sealed class TestPlacementExtension(
        IReadOnlyCollection<CaptionPlacementPolicyRegistration> registrations)
        : CaptionPlacementPolicyExtension
    {
        public override IReadOnlyCollection<CaptionPlacementPolicyRegistration> Registrations
            => registrations;
    }

    private sealed class TestCodec : ICaptionDecoder, ICaptionEncoder
    {
        public CaptionImportResult Decode(string content)
            => CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), content),
            ]));

        public string Encode(CaptionDocument document)
            => string.Join('\n', document.Cues.Select(cue => cue.Text));
    }

    private sealed class PrefixDecoder(string prefix) : ICaptionDecoder
    {
        public CaptionImportResult Decode(string content)
            => CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(1),
                    $"{prefix}:{content}"),
            ]));
    }

    private sealed class BlockingCodecExtension : CaptionDecoderExtension, ICaptionDecoder
    {
        private readonly ManualResetEventSlim _decodeStarted = new();
        private readonly ManualResetEventSlim _releaseDecode = new();
        private readonly IReadOnlyCollection<CaptionDecoderRegistration> _registrations;

        public BlockingCodecExtension(CaptionFormatId format)
        {
            _registrations =
            [
                new CaptionDecoderRegistration(format, this),
            ];
        }

        public override IReadOnlyCollection<CaptionDecoderRegistration> Registrations
            => _registrations;

        public bool WaitForDecode(TimeSpan timeout) => _decodeStarted.Wait(timeout);

        public void ReleaseDecode() => _releaseDecode.Set();

        public CaptionImportResult Decode(string content)
        {
            _decodeStarted.Set();
            if (!_releaseDecode.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocking codec was not released.");
            return CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), content),
            ]));
        }
    }

    private sealed class EncoderOnlyCodecExtension : CaptionEncoderExtension, ICaptionEncoder
    {
        private readonly IReadOnlyCollection<CaptionEncoderRegistration> _registrations;

        public EncoderOnlyCodecExtension(CaptionFormatId format)
        {
            _registrations =
            [
                new CaptionEncoderRegistration(format, this),
            ];
        }

        public override IReadOnlyCollection<CaptionEncoderRegistration> Registrations
            => _registrations;

        public string Encode(CaptionDocument document)
            => string.Join('\n', document.Cues.Select(cue => cue.Text));
    }

    private sealed class BlockingEncoderExtension : CaptionEncoderExtension, ICaptionEncoder
    {
        private readonly ManualResetEventSlim _encodeStarted = new();
        private readonly ManualResetEventSlim _releaseEncode = new();
        private readonly IReadOnlyCollection<CaptionEncoderRegistration> _registrations;

        public BlockingEncoderExtension(CaptionFormatId format)
        {
            _registrations = [new CaptionEncoderRegistration(format, this)];
        }

        public override IReadOnlyCollection<CaptionEncoderRegistration> Registrations
            => _registrations;

        public bool WaitForEncode(TimeSpan timeout) => _encodeStarted.Wait(timeout);

        public void ReleaseEncode() => _releaseEncode.Set();

        public string Encode(CaptionDocument document)
        {
            _encodeStarted.Set();
            if (!_releaseEncode.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocking encoder was not released.");
            return string.Join('\n', document.Cues.Select(cue => cue.Text));
        }
    }
}
