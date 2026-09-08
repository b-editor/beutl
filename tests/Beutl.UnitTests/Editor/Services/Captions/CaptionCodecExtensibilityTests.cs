using System.Text;
using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionCodecExtensibilityTests
{
    private static readonly CaptionFormatId s_pipeFormat = new("example.pipe");

    [Test]
    public async Task Registry_CustomSlotsResolveAndNotifyAStableMetadataList()
    {
        await using var registry = new CaptionCodecRegistry();
        var codec = new PipeCaptionCodec();
        var metadata = registry.Codecs;
        int changes = 0;
        metadata.CollectionChanged += (_, _) => changes++;

        await using ICaptionCodecDescriptorRegistration descriptor = registry.Register(
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"])));
        await using ICaptionDecoderRegistration decoder = registry.Register(
            new CaptionDecoderRegistration(s_pipeFormat, codec));
        await using ICaptionEncoderRegistration encoder = registry.Register(
            new CaptionEncoderRegistration(s_pipeFormat, codec));

        CaptionCodecInfo registered = registry.GetRequired(s_pipeFormat);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Codecs, Is.SameAs(metadata));
            Assert.That(changes, Is.EqualTo(3));
            Assert.That(registered.CanDecode, Is.True);
            Assert.That(registered.CanEncode, Is.True);
            Assert.That(registered.FileExtensions, Is.EqualTo(new[] { ".pipe" }));
            Assert.That(
                registry.TryGetByFileName("captions.PIPE", out CaptionCodecInfo? byFileName),
                Is.True);
            Assert.That(byFileName!.Format, Is.EqualTo(s_pipeFormat));
        }
    }

    [Test]
    public async Task FileNameLookupPrefersTheLongestRegisteredCompoundSuffix()
    {
        var jsonFormat = new CaptionFormatId("json");
        var compoundFormat = new CaptionFormatId("captions-json");
        await using var registry = new CaptionCodecRegistry(
        [
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(jsonFormat, [".json"])),
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(compoundFormat, [".captions.json"])),
        ], [], []);

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGetByFileName("movie.CAPTIONS.JSON", out CaptionCodecInfo? compound), Is.True);
            Assert.That(compound!.Format, Is.EqualTo(compoundFormat));
            Assert.That(registry.TryGetByFileName("movie.json", out CaptionCodecInfo? json), Is.True);
            Assert.That(json!.Format, Is.EqualTo(jsonFormat));
        });
    }

    [Test]
    public async Task Serializer_CustomCapabilitiesCanCreateSuccessAndFailureResults()
    {
        var codec = new PipeCaptionCodec();
        await using var registry = new CaptionCodecRegistry(
            [new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"]))],
            [new CaptionDecoderRegistration(s_pipeFormat, codec)],
            [new CaptionEncoderRegistration(s_pipeFormat, codec)]);
        var serializer = new CaptionDocumentSerializer(registry);

        CaptionImportResult success = serializer.Import(
            Encoding.UTF8.GetBytes("custom text"),
            s_pipeFormat);
        byte[] exported = serializer.Export(success.Document!, s_pipeFormat);
        CaptionImportResult failure = serializer.Import(
            Encoding.UTF8.GetBytes("!invalid"),
            s_pipeFormat);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(success.IsSuccess, Is.True);
            Assert.That(success.Document![0].Text, Is.EqualTo("custom text"));
            Assert.That(Encoding.UTF8.GetString(exported), Is.EqualTo("custom text"));
            Assert.That(failure.IsSuccess, Is.False);
            Assert.That(failure.Diagnostics, Has.One.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidStructure
                && error.Message == "Custom codec rejected the content."));
        }
    }

    [Test]
    public async Task DescriptorlessCapabilitySupportsExactLookupButIsNotPublished()
    {
        var codec = new PipeCaptionCodec();
        await using var registry = new CaptionCodecRegistry(
            [],
            [new CaptionDecoderRegistration(s_pipeFormat, codec)],
            []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Codecs, Is.Empty);
            Assert.That(registry.TryGet(s_pipeFormat, out _), Is.False);
            Assert.That(registry.Decode(s_pipeFormat, "text").IsSuccess, Is.True);
            Assert.Throws<NotSupportedException>(() =>
                registry.Encode(new CaptionFormatId("example.pipe"), new CaptionDocument()));
        }
    }

    [Test]
    public async Task DescriptorOnlyFormatIsPublishedWithoutExecutableDirections()
    {
        await using var registry = new CaptionCodecRegistry(
            [new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"]))],
            [],
            []);

        CaptionCodecInfo info = registry.Codecs.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(info.CanDecode, Is.False);
            Assert.That(info.CanEncode, Is.False);
            Assert.Throws<NotSupportedException>(() => registry.Decode(s_pipeFormat, "text"));
            Assert.Throws<NotSupportedException>(() =>
                registry.Encode(s_pipeFormat, new CaptionDocument()));
        }
    }

    [Test]
    public async Task DecoderAndEncoderReplaceAndRestoreIndependently()
    {
        var baseCodec = new TaggedCodec("base");
        var decoderReplacement = new TaggedCodec("decoder");
        var encoderReplacement = new TaggedCodec("encoder");
        await using var registry = new CaptionCodecRegistry(
            [new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"], order: -20))],
            [new CaptionDecoderRegistration(s_pipeFormat, baseCodec)],
            [new CaptionEncoderRegistration(s_pipeFormat, baseCodec)]);
        ICaptionDecoderRegistration decoder = registry.Register(
            new CaptionDecoderRegistration(
                s_pipeFormat,
                decoderReplacement,
                CaptionCodecRegistrationMode.Replace));
        ICaptionEncoderRegistration encoder = registry.Register(
            new CaptionEncoderRegistration(
                s_pipeFormat,
                encoderReplacement,
                CaptionCodecRegistrationMode.Replace));
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text"),
        ]);
        try
        {
            CaptionCodecInfo info = registry.GetRequired(s_pipeFormat);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(info.FileExtensions, Is.EqualTo(new[] { ".pipe" }));
                Assert.That(info.Order, Is.EqualTo(-20));
                Assert.That(registry.Decode(s_pipeFormat, "text").Document![0].Text,
                    Is.EqualTo("decoder:text"));
                Assert.That(registry.Encode(s_pipeFormat, document), Is.EqualTo("encoder:text"));
            }

            await decoder.DisposeAsync();
            Assert.That(registry.Decode(s_pipeFormat, "text").Document![0].Text,
                Is.EqualTo("base:text"));
            Assert.That(registry.Encode(s_pipeFormat, document), Is.EqualTo("encoder:text"));

            await encoder.DisposeAsync();
            Assert.That(registry.Encode(s_pipeFormat, document), Is.EqualTo("base:text"));
        }
        finally
        {
            await decoder.DisposeAsync();
            await encoder.DisposeAsync();
        }
    }

    [Test]
    public async Task DescriptorRemovalLeavesExactCapabilitiesAvailable()
    {
        var codec = new PipeCaptionCodec();
        await using var registry = new CaptionCodecRegistry();
        ICaptionCodecDescriptorRegistration descriptor = registry.Register(
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"])));
        await using ICaptionDecoderRegistration decoder = registry.Register(
            new CaptionDecoderRegistration(s_pipeFormat, codec));
        await using ICaptionEncoderRegistration encoder = registry.Register(
            new CaptionEncoderRegistration(s_pipeFormat, codec));

        await descriptor.DisposeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Codecs, Is.Empty);
            Assert.That(registry.TryGetByFileExtension(".pipe", out _), Is.False);
            Assert.That(registry.Decode(s_pipeFormat, "text").IsSuccess, Is.True);
            Assert.That(registry.Encode(
                s_pipeFormat,
                new CaptionDocument([
                    new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text"),
                ])), Is.EqualTo("text"));
        }
    }

    [Test]
    public async Task RegistrationModesAreValidatedPerSlot()
    {
        var codec = new PipeCaptionCodec();
        await using var registry = new CaptionCodecRegistry();
        await using ICaptionCodecDescriptorRegistration descriptor = registry.Register(
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"])));
        await using ICaptionDecoderRegistration decoder = registry.Register(
            new CaptionDecoderRegistration(s_pipeFormat, codec));

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(() => registry.Register(
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(s_pipeFormat, [".other"]))));
            Assert.Throws<ArgumentException>(() => registry.Register(
                new CaptionDecoderRegistration(s_pipeFormat, codec)));
            Assert.Throws<ArgumentException>(() => registry.Register(
                new CaptionEncoderRegistration(
                    s_pipeFormat,
                    codec,
                    CaptionCodecRegistrationMode.Replace)));
        }
    }

    [Test]
    public async Task DescriptorOrderAndExtensionCollisionsAreDeterministic()
    {
        var other = new CaptionFormatId("example.other");
        await using var registry = new CaptionCodecRegistry(
            [
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(s_pipeFormat, [".pipe"], order: 20)),
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(other, [".other"], order: 0)),
            ],
            [],
            []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.Codecs.Select(codec => codec.Format),
                Is.EqualTo(new[] { other, s_pipeFormat }));
            Assert.Throws<ArgumentException>(() => registry.Register(
                new CaptionCodecDescriptorRegistration(
                    new CaptionCodecDescriptor(
                        new CaptionFormatId("duplicate"),
                        ["PIPE"]))));
        }
    }

    [Test]
    public async Task DecoderRetirementDoesNotWaitForAnActiveEncoderLease()
    {
        var decoder = new PipeCaptionCodec();
        var encoder = new BlockingEncoder();
        await using var registry = new CaptionCodecRegistry();
        ICaptionDecoderRegistration decoderRegistration = registry.Register(
            new CaptionDecoderRegistration(s_pipeFormat, decoder));
        await using ICaptionEncoderRegistration encoderRegistration = registry.Register(
            new CaptionEncoderRegistration(s_pipeFormat, encoder));
        Task<string> encoding = Task.Run(() => registry.Encode(
            s_pipeFormat,
            new CaptionDocument([
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text"),
            ])));
        Assert.That(encoder.Started.Wait(TimeSpan.FromSeconds(5)), Is.True);

        try
        {
            await decoderRegistration.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            encoder.Release.Set();
        }

        Assert.That(await encoding.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo("text"));
    }

    [Test]
    public async Task EqualDecoderReplacementEntriesKeepReferenceIdentityForOwnerDrains()
    {
        var baseCodec = new PipeCaptionCodec();
        var replacementCodec = new ResettableBlockingDecoder();
        await using var registry = new CaptionCodecRegistry(
            [],
            [new CaptionDecoderRegistration(s_pipeFormat, baseCodec)],
            []);
        var replacementRegistration = new CaptionDecoderRegistration(
            s_pipeFormat,
            replacementCodec,
            CaptionCodecRegistrationMode.Replace);
        ICaptionDecoderRegistration first = registry.Register(replacementRegistration);
        ICaptionDecoderRegistration second = registry.Register(replacementRegistration);
        try
        {
            await AssertRetirementWaitsForDecode(second, replacementCodec);
            replacementCodec.Reset();
            await AssertRetirementWaitsForDecode(first, replacementCodec);
        }
        finally
        {
            replacementCodec.Release.Set();
            await second.DisposeAsync();
            await first.DisposeAsync();
        }

        async Task AssertRetirementWaitsForDecode(
            ICaptionDecoderRegistration registration,
            ResettableBlockingDecoder blocking)
        {
            Task<CaptionImportResult> decode = Task.Run(() =>
                registry.Decode(s_pipeFormat, "text"));
            Assert.That(blocking.Started.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Task retirement = registration.DisposeAsync().AsTask();
            await Task.Yield();
            Assert.That(retirement.IsCompleted, Is.False);
            blocking.Release.Set();
            await Task.WhenAll(decode, retirement).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task RegistryDisposeWaitsForLeaseFromStateRetiredByDescriptorChange()
    {
        var decoder = new ResettableBlockingDecoder();
        var registry = new CaptionCodecRegistry(
            [new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".old"]))],
            [new CaptionDecoderRegistration(s_pipeFormat, decoder)],
            []);
        Task<CaptionImportResult> decode = Task.Run(() => registry.Decode(s_pipeFormat, "text"));
        Assert.That(decoder.Started.Wait(TimeSpan.FromSeconds(5)), Is.True);
        await using ICaptionCodecDescriptorRegistration replacement = registry.Register(
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".new"]),
                CaptionCodecRegistrationMode.Replace));
        Task disposal = registry.DisposeAsync().AsTask();
        await Task.Yield();
        Assert.That(disposal.IsCompleted, Is.False);

        decoder.Release.Set();
        await Task.WhenAll(decode, disposal).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task MetadataObserverFailureDoesNotRollBackRegistryCommit()
    {
        await using var registry = new CaptionCodecRegistry();
        registry.Codecs.CollectionChanged += (_, _) =>
            throw new InvalidOperationException("Observer failure");

        Assert.DoesNotThrow(() => registry.Register(
            new CaptionCodecDescriptorRegistration(
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"]))));
        Assert.That(registry.GetRequired(s_pipeFormat).FileExtensions,
            Is.EqualTo(new[] { ".pipe" }));
    }

    [Test]
    public void DiagnosticKind_AllowsThirdPartyIdentifiers()
    {
        var customKind = new CaptionDiagnosticKind("example.pipe.invalid-token");
        var error = new CaptionDiagnostic(customKind, 4, "Invalid token.");

        Assert.That(error.Kind, Is.EqualTo(customKind));
    }

    private class PipeCaptionCodec : ICaptionDecoder, ICaptionEncoder
    {
        public virtual CaptionImportResult Decode(string content)
        {
            if (content.StartsWith('!'))
            {
                return CaptionImportResult.Failure(new CaptionDiagnostic(
                    CaptionDiagnosticKinds.InvalidStructure,
                    1,
                    "Custom codec rejected the content."));
            }

            return CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), content),
            ]));
        }

        public virtual string Encode(CaptionDocument document) => document[0].Text;
    }

    private sealed class TaggedCodec(string tag) : PipeCaptionCodec
    {
        public override CaptionImportResult Decode(string content)
            => base.Decode($"{tag}:{content}");

        public override string Encode(CaptionDocument document)
            => $"{tag}:{base.Encode(document)}";
    }

    private sealed class BlockingEncoder : ICaptionEncoder
    {
        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public string Encode(CaptionDocument document)
        {
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocking encoder was not released.");
            return document[0].Text;
        }
    }

    private sealed class ResettableBlockingDecoder : ICaptionDecoder
    {
        public ManualResetEventSlim Started { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public void Reset()
        {
            Started.Reset();
            Release.Reset();
        }

        public CaptionImportResult Decode(string content)
        {
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocking decoder was not released.");
            return CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), content),
            ]));
        }
    }
}
