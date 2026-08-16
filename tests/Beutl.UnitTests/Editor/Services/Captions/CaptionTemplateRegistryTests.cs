namespace Beutl.UnitTests.Editor.Services.Captions;

using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Editor.Services.Captions;

[TestFixture]
public sealed class CaptionTemplateRegistryTests
{
    private static readonly CaptionTemplateProviderId s_provider = new("beutl.tests");

    [Test]
    public void Register_RejectsCaseInsensitiveIdentifierCollision()
    {
        var registry = new CaptionTemplateRegistry(
        [
            CreateText("Vendor.Template", "First"),
        ]);

        Assert.That(
            async () => await registry.RegisterAsync(CreateText("vendor.template", "Second")),
            Throws.ArgumentException.With.Message.Contains("already registered"));
    }

    [Test]
    public async Task Register_RequiresExplicitReplacementOfExistingTemplate()
    {
        CaptionTemplateContribution original = CaptionTemplateDefaults.CreateDefaultText("Default");
        CaptionTemplateContribution replacement = CaptionTemplateDefaults.CreateText(
            CaptionTemplateIds.DefaultText,
            s_provider,
            "Replacement");
        var registry = new CaptionTemplateRegistry([original]);

        await registry.RegisterAsync(replacement, CaptionTemplateRegistrationMode.Replace);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                registry.GetRequired(CaptionTemplateIds.DefaultText).Name,
                Is.EqualTo("Replacement"));
            Assert.That(
                async () => await registry.RegisterAsync(
                    CreateText("beutl.tests.missing", "Missing"),
                    CaptionTemplateRegistrationMode.Replace),
                Throws.ArgumentException.With.Message.Contains("is not registered"));
        }
    }

    [Test]
    public void Templates_AreOrderedByExplicitOrderThenStableIdentifier()
    {
        var registry = new CaptionTemplateRegistry(
        [
            CreateText("beutl.tests.zulu", "Zulu", order: 10),
            CreateText("beutl.tests.beta", "Beta"),
            CreateText("beutl.tests.alpha", "Alpha"),
        ]);

        Assert.That(
            registry.Templates.Select(template => template.Name),
            Is.EqualTo(new[] { "Alpha", "Beta", "Zulu" }));
    }

    [Test]
    public async Task Register_MetadataObserverFailureDoesNotInterruptRegistryReplacement()
    {
        var registry = new CaptionTemplateRegistry();
        registry.Templates.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("Observer failure");

        Assert.DoesNotThrowAsync(async () =>
            await registry.RegisterAsync(CreateText("beutl.tests.safe", "Safe")));
        Assert.That(registry.GetRequired(new CaptionTemplateId("beutl.tests.safe")).Name, Is.EqualTo("Safe"));
    }

    [Test]
    public async Task Catalog_ComposesCodecAndTemplateExtensionsForNonUiConsumers()
    {
        var format = new CaptionFormatId("beutl.tests.caption");
        CaptionTemplateContribution template = CreateText(
            "beutl.tests.catalog-template",
            "Catalog template");
        var codecExtension = new TestCodecExtension(format);
        var templateExtension = new TestTemplateExtension(
        [
            new CaptionTemplateRegistration(template),
        ]);

        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions);
        extensions.AddExtensions(1, [codecExtension, templateExtension]);
        try
        {
            CaptionImportResult imported = catalog.Serializer.Import(
                "From extension"u8,
                format);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(imported.IsSuccess, Is.True);
                Assert.That(imported.Document!.Cues.Single().Text, Is.EqualTo("From extension"));
                Assert.That(catalog.Templates.GetRequired(template.Id).Name, Is.EqualTo(template.Name));
            }
        }
        finally
        {
            extensions.RemoveExtensions(1);
        }
    }

    [Test]
    public async Task Catalog_DiscardsAnInvalidExtensionAtomically()
    {
        CaptionTemplateContribution valid = CreateText(
            "beutl.tests.valid-before-collision",
            "Valid before collision");
        CaptionTemplateContribution duplicateDefault = CaptionTemplateDefaults.CreateText(
            CaptionTemplateIds.DefaultText,
            s_provider,
            "Unexpected replacement");
        var extension = new TestTemplateExtension(
        [
            new CaptionTemplateRegistration(valid),
            new CaptionTemplateRegistration(duplicateDefault),
        ]);
        var failures = new List<CaptionCatalogExtensionFailure>();

        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose(
            "Default",
            [],
            extensions,
            failures.Add);
        extensions.AddExtensions(1, [extension]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0].Kind, Is.EqualTo(CaptionCatalogContributionKind.Template));
            Assert.That(catalog.Templates.TryGet(valid.Id, out _), Is.False);
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
        var invalid = new TestTemplateExtension([null!]);

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
        var codecExtension = new TestCodecExtension(new CaptionFormatId("beutl.tests.independent-codec"));
        CaptionTemplateContribution template = CreateText(
            "beutl.tests.independent-template",
            "Independent template");
        extensions.AddExtensions(1, [codecExtension]);
        extensions.AddExtensions(2, [new TestTemplateExtension(
        [
            new CaptionTemplateRegistration(template),
        ])]);
        using CaptionTemplateLease lease = catalog.Templates.Acquire(template.Id);

        ExtensionRemoval removal = extensions.RemoveExtensions(1);
        await removal.DrainAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(catalog.Codecs.TryGet(new CaptionFormatId("beutl.tests.independent-codec"), out _), Is.False);
    }

    [Test]
    public async Task Catalog_RemovingCodecExtensionDoesNotWaitForAnotherPackageCodecLease()
    {
        var extensions = new ExtensionProvider();
        await using CaptionCatalog catalog = CaptionCatalog.Compose("Default", [], extensions);
        var removedFormat = new CaptionFormatId("beutl.tests.removed-codec");
        var activeFormat = new CaptionFormatId("beutl.tests.active-codec");
        extensions.AddExtensions(1, [new TestCodecExtension(removedFormat)]);
        var blocking = new BlockingCodecExtension(activeFormat);
        extensions.AddExtensions(2, [blocking]);

        Task<CaptionImportResult> decode = Task.Run(() =>
            catalog.Serializer.Import("caption"u8, activeFormat));
        Assert.That(blocking.WaitForDecode(TimeSpan.FromSeconds(5)), Is.True);
        try
        {
            ExtensionRemoval removal = extensions.RemoveExtensions(1);
            await removal.DrainAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            blocking.ReleaseDecode();
        }

        Assert.That((await decode).IsSuccess, Is.True);
    }

    [Test]
    public async Task Catalog_RemovingMergedDecoderOwnerWaitsForItsActiveLease()
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
        CaptionTemplateContribution template = CreateText(
            "beutl.tests.dispose-template",
            "Dispose template");
        var catalog = CaptionCatalog.Compose("Default", [], extensions);
        extensions.AddExtensions(1, [new TestTemplateExtension(
        [
            new CaptionTemplateRegistration(template),
        ])]);
        using CaptionTemplateLease lease = catalog.Templates.Acquire(template.Id);

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

    private static CaptionTemplateContribution CreateText(
        string id,
        string name,
        int order = 0)
        => CaptionTemplateDefaults.CreateText(
            new CaptionTemplateId(id),
            s_provider,
            name,
            order);

    private sealed class TestCodecExtension : CaptionCodecExtension
    {
        private readonly IReadOnlyCollection<CaptionCodecRegistration> _registrations;

        public TestCodecExtension(CaptionFormatId format)
        {
            var codec = new TestCodec();
            _registrations =
            [
                new CaptionCodecRegistration(new CaptionCodecContribution(
                    format,
                    new CaptionCodecDescriptor(format, [".test-caption"]),
                    codec,
                    codec)),
            ];
        }

        public override IReadOnlyCollection<CaptionCodecRegistration> Registrations
            => _registrations;
    }

    private sealed class TestTemplateExtension(
        IReadOnlyCollection<CaptionTemplateRegistration> registrations)
        : CaptionTemplateExtension
    {
        public override IReadOnlyCollection<CaptionTemplateRegistration> Registrations
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

    private sealed class BlockingCodecExtension : CaptionCodecExtension, ICaptionDecoder
    {
        private readonly ManualResetEventSlim _decodeStarted = new();
        private readonly ManualResetEventSlim _releaseDecode = new();
        private readonly IReadOnlyCollection<CaptionCodecRegistration> _registrations;

        public BlockingCodecExtension(CaptionFormatId format)
        {
            _registrations =
            [
                new CaptionCodecRegistration(new CaptionCodecContribution(
                    format,
                    new CaptionCodecDescriptor(format, [$".{format.Value}.caption"]),
                    decoder: this)),
            ];
        }

        public override IReadOnlyCollection<CaptionCodecRegistration> Registrations => _registrations;

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

    private sealed class EncoderOnlyCodecExtension : CaptionCodecExtension, ICaptionEncoder
    {
        private readonly IReadOnlyCollection<CaptionCodecRegistration> _registrations;

        public EncoderOnlyCodecExtension(CaptionFormatId format)
        {
            _registrations =
            [
                new CaptionCodecRegistration(
                    new CaptionCodecContribution(format, encoder: this),
                    CaptionCodecRegistrationMode.Merge),
            ];
        }

        public override IReadOnlyCollection<CaptionCodecRegistration> Registrations => _registrations;

        public string Encode(CaptionDocument document)
            => string.Join('\n', document.Cues.Select(cue => cue.Text));
    }
}
