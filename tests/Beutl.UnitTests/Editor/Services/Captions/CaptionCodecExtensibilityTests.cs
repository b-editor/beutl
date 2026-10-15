using System.Text;
using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionCodecExtensibilityTests
{
    private static readonly CaptionFormatId s_pipeFormat = new("example.pipe");

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
    public async Task DescriptorOrderIsDeterministic()
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

        Assert.That(registry.Codecs.Select(codec => codec.Format),
            Is.EqualTo(new[] { other, s_pipeFormat }));
    }

    private class PipeCaptionCodec : ICaptionDecoder, ICaptionEncoder
    {
        public virtual CaptionImportResult Decode(string content)
        {
            if (content.StartsWith('!'))
            {
                return CaptionImportResult.Failure([
                    new CaptionDiagnostic(
                        CaptionDiagnosticKinds.InvalidStructure,
                        "Custom codec rejected the content."),
                ]);
            }

            return CaptionImportResult.Imported(new CaptionDocument(
            [
                new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), content),
            ]));
        }

        public virtual string Encode(CaptionDocument document) => document[0].Text;
    }
}
