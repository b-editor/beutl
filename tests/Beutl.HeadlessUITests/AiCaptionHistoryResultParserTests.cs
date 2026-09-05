using System.Text;
using Beutl.Api.Services;
using Beutl.Services.AI;
using Beutl.ViewModels.Dialogs;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiCaptionHistoryResultParserTests
{
    [Test]
    public void TryParse_TranscriptionRestoresTimedSegments()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "version": 1,
              "kind": "stt",
              "language": "JA",
              "segments": [
                { "start": 0.5, "end": 1.25, "text": "First" },
                { "start": 2, "end": 3.5, "text": "Second" }
              ]
            }
            """);

        bool parsed = AiCaptionHistoryResultParser.TryParse(
            bytes,
            "stt",
            new AiJobId("job-stt"),
            out AiCaptionHistoryResult? result);

        Assert.That(parsed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.JobId, Is.EqualTo(new AiJobId("job-stt")));
            Assert.That(result.Language, Is.EqualTo("ja"));
            Assert.That(result.Segments.Select(segment => segment.Text),
                Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(result.Segments[0].Start, Is.EqualTo(0.5));
            Assert.That(result.Segments[1].End, Is.EqualTo(3.5));
        }
    }

    [TestCase("1e20", "1e20")]
    [TestCase("1", "1e20")]
    public void TryParse_RejectsTimestampsOutsideTimeSpanRange(string start, string end)
    {
        byte[] bytes = Encoding.UTF8.GetBytes($$"""
            {
              "version": 1,
              "kind": "stt",
              "segments": [
                { "start": {{start}}, "end": {{end}}, "text": "Invalid" }
              ]
            }
            """);

        Assert.That(AiCaptionHistoryResultParser.TryParse(
            bytes,
            "stt",
            new AiJobId("job-overflow"),
            out _), Is.False);
    }

    [Test]
    public void TryParse_TranslationWithoutTimingsKeepsItsOrderAndIsCollectable()
    {
        // Untimed translations must remain collectable in input order.
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "version": 1,
              "kind": "translation",
              "targetLanguage": "ja",
              "segments": [
                { "id": "1", "text": "First" },
                { "id": "2", "text": "Second" }
              ]
            }
            """);

        bool parsed = AiCaptionHistoryResultParser.TryParse(
            bytes,
            "translation",
            new AiJobId("job-translation"),
            out AiCaptionHistoryResult? result);

        Assert.That(parsed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Segments.Select(segment => segment.Text),
                Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(result.Segments[0].Start, Is.EqualTo(0));
            Assert.That(
                result.Segments[1].Start,
                Is.GreaterThan(result.Segments[0].End - 0.001),
                "Synthetic ranges must preserve input order.");
            Assert.That(result.Language, Is.EqualTo("ja"));
        }
    }

    [Test]
    public void TryParse_TranslationWithMixedContextPreservesKnownTimingsAndPlacesUntimedCuesSafely()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "version": 1,
              "kind": "translation",
              "targetLanguage": "ja",
              "segments": [
                {
                  "id": "1",
                  "text": "First",
                  "context": { "groupId": "g1", "partIndex": 0, "start": 4, "end": 6 }
                },
                { "id": "2", "text": "Second" },
                {
                  "id": "3",
                  "text": "Earlier timed",
                  "context": { "groupId": "g0", "partIndex": 0, "start": 1, "end": 2 }
                },
                { "id": "4", "text": "Fourth" }
              ]
            }
            """);

        bool parsed = AiCaptionHistoryResultParser.TryParse(
            bytes,
            "translation",
            new AiJobId("job-translation"),
            out AiCaptionHistoryResult? result);

        Assert.That(parsed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Segments.Select(segment => segment.Text),
                Is.EqualTo(new[] { "Earlier timed", "First", "Second", "Fourth" }));
            Assert.That(result.Segments.Select(segment => segment.Start),
                Is.EqualTo(new[] { 1d, 4d, 6d, 7d }));
            Assert.That(result.Segments.Select(segment => segment.End),
                Is.EqualTo(new[] { 2d, 6d, 7d, 8d }));
        }
    }

    [Test]
    public void TryParse_TranslationReassemblesTimedPartsByGroup()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "version": 1,
              "kind": "translation",
              "sourceLanguage": "en",
              "targetLanguage": "ja",
              "segments": [
                {
                  "id": "c1-p5",
                  "text": "Second",
                  "context": { "groupId": "c1", "partIndex": 5, "start": 4, "end": 5 }
                },
                {
                  "id": "c0-p1",
                  "text": " world",
                  "context": { "groupId": "c0", "partIndex": 1, "start": 1, "end": 3 }
                },
                {
                  "id": "c0-p0",
                  "text": "Hello",
                  "context": { "groupId": "c0", "partIndex": 0, "start": 1, "end": 3 }
                }
              ]
            }
            """);

        bool parsed = AiCaptionHistoryResultParser.TryParse(
            bytes,
            "translation",
            new AiJobId("job-translation"),
            out AiCaptionHistoryResult? result);

        Assert.That(parsed, Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Language, Is.EqualTo("ja"));
            Assert.That(result.Segments.Select(segment => segment.Text),
                Is.EqualTo(new[] { "Hello world", "Second" }));
            Assert.That(result.Segments.Select(segment => segment.Start),
                Is.EqualTo(new[] { 1d, 4d }));
        }
    }

    [TestCase("stt", "{ \"version\": 1, \"kind\": \"stt\", \"segments\": [{ \"start\": 1, \"end\": 1, \"text\": \"bad\" }] }")]
    [TestCase("translation", "{ \"version\": 1, \"kind\": \"translation\", \"targetLanguage\": \"ja\", \"segments\": [{ \"id\": \"c0-p1\", \"text\": \"first available part\", \"context\": { \"groupId\": \"c0\", \"partIndex\": 1, \"start\": 0, \"end\": 1 } }, { \"id\": \"c0-p3\", \"text\": \"gap\", \"context\": { \"groupId\": \"c0\", \"partIndex\": 3, \"start\": 0, \"end\": 1 } }] }")]
    public void TryParse_RejectsResultsThatCannotBeSafelyImported(string kind, string json)
    {
        Assert.That(AiCaptionHistoryResultParser.TryParse(
            Encoding.UTF8.GetBytes(json),
            kind,
            new AiJobId("job-invalid"),
            out _), Is.False);
    }

    [Test]
    public void SizeLimitedMemoryStream_RejectsTheFirstWritePastTheLimit()
    {
        using var stream = new SizeLimitedMemoryStream(4);

        stream.Write([1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() => stream.WriteByte(5));
        Assert.That(stream.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void AudioSourceItem_MapsOnlyTheSelectedSourceWindow()
    {
        var source = new AudioSourceItem(
            "source",
            "source.wav",
            TimeSpan.FromSeconds(60),
            elementStart: TimeSpan.FromSeconds(2),
            elementLength: TimeSpan.FromSeconds(5),
            sourceOffset: TimeSpan.FromSeconds(10));

        AiTranscriptionSegment[] mapped = source.MapSegmentsToScene(
        [
            new AiTranscriptionSegment { Start = 10, End = 12, Text = "inside" },
            new AiTranscriptionSegment { Start = 5, End = 9, Text = "outside" },
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapped, Has.Length.EqualTo(1));
            Assert.That(mapped[0].Start, Is.EqualTo(2).Within(1e-9));
            Assert.That(mapped[0].End, Is.EqualTo(4).Within(1e-9));
            Assert.That(mapped[0].Text, Is.EqualTo("inside"));
        }
    }
}
