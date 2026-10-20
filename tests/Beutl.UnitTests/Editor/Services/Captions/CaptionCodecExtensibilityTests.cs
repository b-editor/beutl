using System.Text;
using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionCodecExtensibilityTests
{
    private static readonly CaptionFormatId s_pipeFormat = new("example.pipe");

    [Test]
    public void FileNameLookupPrefersTheLongestRegisteredCompoundSuffix()
    {
        var jsonFormat = new CaptionFormatId("json");
        var compoundFormat = new CaptionFormatId("captions-json");
        var registry = new CaptionCodecRegistry(
            [
                new CaptionCodecDescriptor(jsonFormat, [".json"]),
                new CaptionCodecDescriptor(compoundFormat, [".captions.json"]),
            ],
            new Dictionary<CaptionFormatId, ICaptionDecoder>(),
            new Dictionary<CaptionFormatId, ICaptionEncoder>());

        Assert.Multiple(() =>
        {
            Assert.That(registry.TryGetByFileName("movie.CAPTIONS.JSON", out CaptionCodecInfo? compound), Is.True);
            Assert.That(compound!.Format, Is.EqualTo(compoundFormat));
            Assert.That(registry.TryGetByFileName("movie.json", out CaptionCodecInfo? json), Is.True);
            Assert.That(json!.Format, Is.EqualTo(jsonFormat));
        });
    }

    [Test]
    public void Serializer_CustomCapabilitiesCanCreateSuccessAndFailureResults()
    {
        var codec = new PipeCaptionCodec();
        var registry = new CaptionCodecRegistry(
            [new CaptionCodecDescriptor(s_pipeFormat, [".pipe"])],
            new Dictionary<CaptionFormatId, ICaptionDecoder> { [s_pipeFormat] = codec },
            new Dictionary<CaptionFormatId, ICaptionEncoder> { [s_pipeFormat] = codec });
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
    public void DescriptorOrderIsDeterministic()
    {
        var other = new CaptionFormatId("example.other");
        var registry = new CaptionCodecRegistry(
            [
                new CaptionCodecDescriptor(s_pipeFormat, [".pipe"], order: 20),
                new CaptionCodecDescriptor(other, [".other"], order: 0),
            ],
            new Dictionary<CaptionFormatId, ICaptionDecoder>(),
            new Dictionary<CaptionFormatId, ICaptionEncoder>());

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
