using System.Text;
using Beutl.Api.Services;
using Beutl.Services.AI;

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

    [Test]
    public void TryParse_TranslationWithoutTimingsKeepsItsOrderAndIsCollectable()
    {
        // 行だけを渡して訳したもの——時刻は付いていない。読めないものとして拒むと、
        // 支払い済みの結果を取りに行く道がここで閉じる。並び順のまま、仮の時刻で
        // 受け取って、置き場所は画面で直してもらう。
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
                "仮の時刻でも、順番どおりに並んでいる。");
            Assert.That(result.Language, Is.EqualTo("ja"));
        }
    }

    [Test]
    public void TryParse_TranslationMissingOnlySomeTimingsIsRefused()
    {
        // 一部にだけ時刻があるものは、組み立てられない。並びも時刻も信じられない
        // ので、仮に置くのではなく拒む。
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {
              "version": 1,
              "kind": "translation",
              "targetLanguage": "ja",
              "segments": [
                {
                  "id": "1",
                  "text": "First",
                  "context": { "groupId": "g1", "partIndex": 0, "start": 0, "end": 1 }
                },
                { "id": "2", "text": "Second" }
              ]
            }
            """);

        Assert.That(
            AiCaptionHistoryResultParser.TryParse(
                bytes,
                "translation",
                new AiJobId("job-translation"),
                out _),
            Is.False);
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
}
