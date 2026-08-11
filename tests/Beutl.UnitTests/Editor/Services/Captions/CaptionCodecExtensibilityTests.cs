using System.Text;
using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionCodecExtensibilityTests
{
    private static readonly CaptionFormatId s_pipeFormat = new("example.pipe");

    [Test]
    public void Registry_CustomContributionResolvesByTypedIdAndFileName()
    {
        var codec = new PipeCaptionCodec();
        var contribution = new CaptionCodecContribution(
            s_pipeFormat,
            new CaptionCodecDescriptor(s_pipeFormat, [".pipe"]),
            codec,
            codec);
        CaptionCodecRegistry registry = CaptionCatalog.CreateDefault("Default").Codecs;

        registry.Register(new CaptionCodecRegistration(contribution));

        Assert.Multiple(() =>
        {
            CaptionCodecInfo registered = registry.GetRequired(s_pipeFormat);
            Assert.That(registered.CanDecode, Is.True);
            Assert.That(registered.CanEncode, Is.True);
            Assert.That(registered.FileExtensions, Is.EqualTo(new[] { ".pipe" }));
            Assert.That(
                registry.TryGetByFileName("captions.PIPE", out CaptionCodecInfo? byFileName),
                Is.True);
            Assert.That(byFileName!.Format, Is.EqualTo(s_pipeFormat));
            Assert.That(registry.GetRequired(CaptionFormats.Srt).CanDecode, Is.True);
            Assert.That(registry.GetRequired(CaptionFormats.Srt).CanEncode, Is.True);
        });
    }

    [Test]
    public void Serializer_CustomCapabilitiesCanCreateSuccessAndFailureResults()
    {
        var codec = new PipeCaptionCodec();
        var registry = new CaptionCodecRegistry(
        [
            new CaptionCodecRegistration(new CaptionCodecContribution(
                s_pipeFormat,
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"]),
                codec,
                codec)),
        ]);
        var serializer = new CaptionDocumentSerializer(registry);

        CaptionImportResult success = serializer.Import(
            Encoding.UTF8.GetBytes("custom text"),
            s_pipeFormat);
        byte[] exported = serializer.Export(success.Document!, s_pipeFormat);
        CaptionImportResult failure = serializer.Import(
            Encoding.UTF8.GetBytes("!invalid"),
            s_pipeFormat);

        Assert.Multiple(() =>
        {
            Assert.That(success.IsSuccess, Is.True);
            Assert.That(success.Document![0].Text, Is.EqualTo("custom text"));
            Assert.That(Encoding.UTF8.GetString(exported), Is.EqualTo("custom text"));
            Assert.That(failure.IsSuccess, Is.False);
            Assert.That(failure.Diagnostics, Has.One.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidStructure
                && error.Message == "Custom codec rejected the content."));
        });
    }

    [Test]
    public void Registry_IndependentCapabilitiesReportUnsupportedDirection()
    {
        var registry = new CaptionCodecRegistry(
        [
            new CaptionCodecRegistration(new CaptionCodecContribution(
                s_pipeFormat,
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"]),
                decoder: new PipeCaptionCodec())),
        ]);
        var serializer = new CaptionDocumentSerializer(registry);

        Assert.That(
            serializer.Import(Encoding.UTF8.GetBytes("text"), s_pipeFormat).IsSuccess,
            Is.True);
        Assert.Throws<NotSupportedException>(() =>
            serializer.Export(new CaptionDocument(), s_pipeFormat));
    }

    [Test]
    public void Registry_MergesIndependentDirectionsAndRejectsOnlyDuplicateDirectionsOrDescriptor()
    {
        var decoder = new PipeCaptionCodec();
        var encoder = new PipeCaptionCodec();
        var registry = new CaptionCodecRegistry();
        registry.Register(new CaptionCodecRegistration(
            new CaptionCodecContribution(s_pipeFormat, decoder: decoder)));
        CaptionCodecInfo capabilityOnly = registry.GetRequired(s_pipeFormat);
        Assert.That(capabilityOnly.CanDecode, Is.True);
        Assert.That(capabilityOnly.CanEncode, Is.False);
        Assert.That(capabilityOnly.FileExtensions, Is.Empty);
        registry.Register(new CaptionCodecRegistration(
            new CaptionCodecContribution(
                s_pipeFormat,
                descriptor: new CaptionCodecDescriptor(s_pipeFormat, [".pipe"])),
            CaptionCodecRegistrationMode.Merge));
        registry.Register(new CaptionCodecRegistration(
            new CaptionCodecContribution(s_pipeFormat, encoder: encoder),
            CaptionCodecRegistrationMode.Merge));

        CaptionCodecInfo merged = registry.GetRequired(s_pipeFormat);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(merged.FileExtensions, Is.EqualTo(new[] { ".pipe" }));
            Assert.That(merged.CanDecode, Is.True);
            Assert.That(merged.CanEncode, Is.True);
            Assert.That(registry.Codecs, Has.Count.EqualTo(1));
            Assert.That(registry.TryGetByFileExtension("pipe", out CaptionCodecInfo? byExtension), Is.True);
            Assert.That(byExtension!.Format, Is.EqualTo(merged.Format));
        }

        Assert.Throws<ArgumentException>(() => registry.Register(new CaptionCodecRegistration(
            new CaptionCodecContribution(s_pipeFormat, decoder: new PipeCaptionCodec()),
            CaptionCodecRegistrationMode.Merge)));
        Assert.Throws<ArgumentException>(() => registry.Register(new CaptionCodecRegistration(
            new CaptionCodecContribution(s_pipeFormat, encoder: new PipeCaptionCodec()),
            CaptionCodecRegistrationMode.Merge)));
        Assert.Throws<ArgumentException>(() => registry.Register(new CaptionCodecRegistration(
            new CaptionCodecContribution(
                s_pipeFormat,
                descriptor: new CaptionCodecDescriptor(s_pipeFormat, [".other"])),
            CaptionCodecRegistrationMode.Merge)));
    }

    [Test]
    public void Registry_ConflictingFormatOrExtensionIsRejectedWithoutPartialRegistration()
    {
        var codec = new PipeCaptionCodec();
        var first = new CaptionCodecContribution(
            s_pipeFormat,
            new CaptionCodecDescriptor(s_pipeFormat, [".pipe"]),
            codec,
            codec);
        var registry = new CaptionCodecRegistry([new CaptionCodecRegistration(first)]);

        Assert.Throws<ArgumentException>(() => registry.Register(new CaptionCodecRegistration(
            new CaptionCodecContribution(
                new CaptionFormatId("EXAMPLE.PIPE"),
                new CaptionCodecDescriptor(new CaptionFormatId("EXAMPLE.PIPE"), [".duplicate"]),
                new PipeCaptionCodec()))));
        Assert.Throws<ArgumentException>(() => registry.Register(new CaptionCodecRegistration(
            new CaptionCodecContribution(
                new CaptionFormatId("other"),
                new CaptionCodecDescriptor(new CaptionFormatId("other"), ["PIPE"]),
                new PipeCaptionCodec()))));

        Assert.Multiple(() =>
        {
            Assert.That(registry.Codecs, Has.Count.EqualTo(1));
            Assert.That(registry.TryGet(new CaptionFormatId("other"), out _), Is.False);
        });
    }

    [Test]
    public void Registry_ReplacementAndOrderingAreExplicitAndDeterministic()
    {
        var originalCodec = new PipeCaptionCodec();
        var replacementCodec = new PipeCaptionCodec();
        var otherFormat = new CaptionFormatId("example.other");
        var registry = new CaptionCodecRegistry(
        [
            new CaptionCodecRegistration(new CaptionCodecContribution(
                s_pipeFormat,
                new CaptionCodecDescriptor(s_pipeFormat, [".old"]),
                originalCodec,
                originalCodec,
                order: 20)),
            new CaptionCodecRegistration(new CaptionCodecContribution(
                otherFormat,
                new CaptionCodecDescriptor(otherFormat, [".other"]),
                decoder: replacementCodec,
                order: 0)),
        ]);
        var replacement = new CaptionCodecContribution(
            s_pipeFormat,
            new CaptionCodecDescriptor(s_pipeFormat, [".new"]),
            replacementCodec,
            replacementCodec,
            order: -10);

        registry.Register(new CaptionCodecRegistration(
            replacement,
            CaptionCodecRegistrationMode.Replace));

        using (Assert.EnterMultipleScope())
        {
            CaptionCodecInfo registered = registry.GetRequired(s_pipeFormat);
            Assert.That(registered.FileExtensions, Is.EqualTo(new[] { ".new" }));
            Assert.That(registered.Order, Is.EqualTo(-10));
            Assert.That(registry.TryGetByFileExtension(".old", out _), Is.False);
            Assert.That(registry.TryGetByFileExtension(".new", out var resolved), Is.True);
            Assert.That(resolved!.Format, Is.EqualTo(s_pipeFormat));
            Assert.That(
                registry.Codecs.Select(codec => codec.Format),
                Is.EqualTo(new[] { s_pipeFormat, otherFormat }));
        }
    }

    [Test]
    public void DiagnosticKind_AllowsThirdPartyIdentifiers()
    {
        var customKind = new CaptionDiagnosticKind("example.pipe.invalid-token");
        var error = new CaptionDiagnostic(customKind, 4, "Invalid token.");

        Assert.That(error.Kind, Is.EqualTo(customKind));
    }

    private sealed class PipeCaptionCodec : ICaptionDecoder, ICaptionEncoder
    {
        public CaptionImportResult Decode(string content)
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

        public string Encode(CaptionDocument document) => document[0].Text;
    }
}
