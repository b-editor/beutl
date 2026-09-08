using System.Text;
using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class CaptionDocumentSerializerTests
{
    private readonly CaptionDocumentSerializer _serializer =
        CaptionCatalog.CreateDefault("Default").Serializer;

    [Test]
    public void Import_InvalidUtf8_ReturnsFailureWithoutThrowing()
    {
        byte[] invalidUtf8 = [0xC3, 0x28];

        CaptionImportResult result = _serializer.Import(invalidUtf8, CaptionFormats.Srt);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Document, Is.Null);
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidUtf8 && error.LineNumber is null));
        });
    }

    [Test]
    public void ImportSrt_BomUnicodeMultilineAndArrowText_ArePreserved()
    {
        const string source = "\uFEFF1\r\n00:00:01,250 --> 00:00:03,500\r\nこんにちは & <world>\r\ntext --> text\r\n\r\n";

        CaptionImportResult result = Import(source, CaptionFormats.Srt);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document!.Cues, Has.Count.EqualTo(1));
            Assert.That(result.Document[0], Is.EqualTo(new CaptionCue(
                TimeSpan.FromMilliseconds(1250),
                TimeSpan.FromMilliseconds(3500),
                "こんにちは & <world>\ntext --> text")));
        });
    }

    [Test]
    public void ImportSrt_WhitespaceOnlyBlankLinesSeparateCuesAndMalformedBlocks()
    {
        const string source = " \t\n"
                              + "1\n00:00:00,000 --> 00:00:01,000\nfirst\n\t \n"
                              + "not a cue\nignored\n \t\n"
                              + "2\n00:00:01,000 --> 00:00:02,000\nsecond\n";

        CaptionImportResult result = Import(source, CaptionFormats.Srt);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document!.Cues.Select(cue => cue.Text),
                Is.EqualTo(new[] { "first", "second" }));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.InvalidStructure));
        });
    }

    [Test]
    public void SrtRoundTrip_UsesUtf8WithoutBomAndSupportsLargeHours()
    {
        TimeSpan start = TimeSpan.FromHours(100) + TimeSpan.FromMilliseconds(1);
        TimeSpan end = start + TimeSpan.FromMilliseconds(999);
        var document = new CaptionDocument(
        [
            new CaptionCue(start, end, "Escaping stays literal: <b>&</b>\n日本語"),
        ]);

        byte[] exported = _serializer.Export(document, CaptionFormats.Srt);
        CaptionImportResult imported = _serializer.Import(exported, CaptionFormats.Srt);

        Assert.Multiple(() =>
        {
            Assert.That(exported.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(Encoding.UTF8.GetString(exported), Does.Contain("100:00:00,001"));
            Assert.That(imported.IsSuccess, Is.True);
            Assert.That(imported.Document!.Cues, Is.EqualTo(document.Cues));
        });
    }

    [Test]
    public void ExportSrt_SubMillisecondCue_ExpandsToRepresentableBoundary()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.FromTicks(1), TimeSpan.FromTicks(2), "short"),
        ]);

        byte[] exported = _serializer.Export(document, CaptionFormats.Srt);
        CaptionImportResult imported = _serializer.Import(exported, CaptionFormats.Srt);

        Assert.Multiple(() =>
        {
            Assert.That(imported.IsSuccess, Is.True);
            Assert.That(imported.Document![0].Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(imported.Document[0].End, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
        });
    }

    [TestCase("00:60:00,000 --> 00:60:01,000")]
    [TestCase("00:00:00.000 --> 00:00:01.000")]
    [TestCase("00:00:01,000 --> 00:00:01,000")]
    [TestCase("99999999999999999999:00:00,000 --> 99999999999999999999:00:01,000")]
    public void ImportSrt_MalformedTiming_RejectsWholeDocument(string timing)
    {
        CaptionImportResult result = Import($"1\n{timing}\ntext\n", CaptionFormats.Srt);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Document, Is.Null);
            Assert.That(result.Diagnostics, Has.Some.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidTiming));
        });
    }

    [Test]
    public void ImportSrt_MixedValidity_PreservesValidCuesWithDiagnostics()
    {
        const string source = """
            1
            00:00:00,000 --> 00:00:01,000
            first

            2
            invalid --> timing
            skipped

            3
            00:00:02,000 --> 00:00:03,000
            third
            """;

        CaptionImportResult result = Import(source, CaptionFormats.Srt);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document!.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "first", "third" }));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.InvalidTiming));
        }
    }

    [Test]
    public void ExportSrt_BlankLineInsideCue_ThrowsWithCueIndex()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "first\n\nsecond"),
        ]);

        CaptionExportException? exception = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(document, CaptionFormats.Srt));

        Assert.That(exception!.CueIndex, Is.Zero);
    }

    [Test]
    public void ExportSrt_WhitespaceOnlyLineInsideCue_ThrowsWithCueIndex()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                "first\n \t\nsecond"),
        ]);

        CaptionExportException? exception = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(document, CaptionFormats.Srt));

        Assert.That(exception!.CueIndex, Is.Zero);
    }

    [Test]
    public void ExportSrt_UnsupportedStructuredFields_ThrowWithCueIndex()
    {
        CaptionDocument[] documents =
        [
            new([new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text", speaker: "Alice")]),
            new([new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text", language: "en")]),
            new([new CaptionCue(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                "text",
                metadata: Metadata(CaptionMetadataKeys.AssStyle, "Default"))]),
        ];

        foreach (CaptionDocument document in documents)
        {
            CaptionExportException? exception = Assert.Throws<CaptionExportException>(() =>
                _serializer.Export(document, CaptionFormats.Srt));
            Assert.That(exception!.CueIndex, Is.Zero);
        }
    }

    [Test]
    public void ImportWebVtt_IdentifiersSettingsMetadataEntitiesAndFormatting_AreHandled()
    {
        const string source = """
            WEBVTT Sample

            NOTE ignored
            note body

            STYLE
            ::cue { color: lime; }

            cue-1
            01:02.003 --> 00:01:04.005 align:start
            <v Tom &amp; Jerry><lang en-US><c.warning.high>Go &lt;now&gt; <b>bold</b></c></lang></v>

            """;

        CaptionImportResult result = Import(source, CaptionFormats.WebVtt);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document![0], Is.EqualTo(new CaptionCue(
                TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(2003),
                TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(4005),
                "Go <now> bold",
                "Tom & Jerry",
                "en-US",
                Metadata(CaptionMetadataKeys.WebVttClasses, "warning.high"))));
        });
    }

    [Test]
    public void WebVttRoundTrip_EscapesTextAndPreservesMetadata()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromSeconds(2),
                "<hello> & 'quoted'\n日本語",
                "A > B & C",
                "ja-JP",
                Metadata(CaptionMetadataKeys.WebVttClasses, "warning.high")),
        ]);

        byte[] exported = _serializer.Export(document, CaptionFormats.WebVtt);
        string text = Encoding.UTF8.GetString(exported);
        CaptionImportResult imported = _serializer.Import(exported, CaptionFormats.WebVtt);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("&lt;hello&gt; &amp;"));
            Assert.That(text, Does.Contain("<v A &gt; B &amp; C>"));
            Assert.That(imported.IsSuccess, Is.True);
            Assert.That(imported.Document!.Cues, Is.EqualTo(document.Cues));
        });
    }

    [Test]
    public void ImportWebVtt_MissingHeaderOrMalformedTiming_ReturnsFailure()
    {
        CaptionImportResult missingHeader = Import("00:00.000 --> 00:01.000\ntext\n", CaptionFormats.WebVtt);
        CaptionImportResult badTiming = Import(
            "WEBVTT\n\n00:00:00.000 --> 00:00:00.000\ntext\n",
            CaptionFormats.WebVtt);

        Assert.Multiple(() =>
        {
            Assert.That(missingHeader.Diagnostics, Has.One.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidHeader));
            Assert.That(badTiming.Diagnostics, Has.One.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidTiming));
            Assert.That(missingHeader.Document, Is.Null);
            Assert.That(badTiming.Document, Is.Null);
        });
    }

    [Test]
    public void ImportWebVtt_MixedValidity_PreservesValidCuesWithDiagnostics()
    {
        const string source = """
            WEBVTT

            00:00.000 --> 00:01.000
            first

            invalid --> timing
            skipped

            00:02.000 --> 00:03.000
            third
            """;

        CaptionImportResult result = Import(source, CaptionFormats.WebVtt);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document!.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "first", "third" }));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.InvalidTiming));
        }
    }

    [Test]
    public void ImportWebVtt_CanonicalizesVoiceAnnotationWhitespace()
    {
        CaptionImportResult result = Import(
            "WEBVTT\n\n00:00.000 --> 00:01.000\n<v\tAlice  Bob\t>text</v>\n",
            CaptionFormats.WebVtt);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Document![0].Speaker, Is.EqualTo("Alice Bob"));
            Assert.That(result.Document[0].Text, Is.EqualTo("text"));
        }
    }

    [Test]
    public void ImportWebVtt_MultipleVoiceSpansProduceAnExplicitDiagnostic()
    {
        CaptionImportResult result = Import(
            "WEBVTT\n\n00:00.000 --> 00:01.000\n<v Alice>Hello</v> <v Bob>Bye</v>\n",
            CaptionFormats.WebVtt);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document![0].Speaker, Is.EqualTo("Alice"));
            Assert.That(result.Document[0].Text, Is.EqualTo("Hello Bye"));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.UnsupportedMarkup
                && diagnostic.LineNumber == 4));
        }
    }

    [Test]
    public void ImportWebVtt_NonLeadingVoiceSpanProducesAnExplicitDiagnostic()
    {
        CaptionImportResult result = Import(
            "WEBVTT\n\n00:00.000 --> 00:01.000\nHello <v Alice>world</v>\n",
            CaptionFormats.WebVtt);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document![0].Speaker, Is.Null);
            Assert.That(result.Document[0].Text, Is.EqualTo("Hello world"));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.UnsupportedMarkup
                && diagnostic.LineNumber == 4));
        }
    }

    [TestCase("<b>bold</b>", "bold")]
    [TestCase("<ruby>漢<rt>かん</rt></ruby>", "漢かん")]
    [TestCase("before <00:00:00.500>after", "before after")]
    public void ImportWebVtt_StrippedInlineMarkupProducesAnExplicitDiagnostic(
        string payload,
        string expectedText)
    {
        CaptionImportResult result = Import(
            $"WEBVTT\n\n00:00.000 --> 00:01.000\n{payload}\n",
            CaptionFormats.WebVtt);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document![0].Text, Is.EqualTo(expectedText));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.UnsupportedMarkup
                && diagnostic.LineNumber == 4));
        }
    }

    [TestCase(" Alice")]
    [TestCase("Alice ")]
    [TestCase("Alice\tBob")]
    [TestCase("Alice  Bob")]
    [TestCase(" \t ")]
    public void ExportWebVtt_NoncanonicalSpeakerWhitespace_ThrowsWithCueIndex(string speaker)
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text", speaker),
        ]);

        CaptionExportException? exception = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(document, CaptionFormats.WebVtt));

        Assert.That(exception!.CueIndex, Is.Zero);
    }

    [Test]
    public void ExportWebVtt_InvalidLanguageOrClass_ThrowsSafely()
    {
        var invalidLanguage = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text", language: "en_US"),
        ]);
        var invalidClass = new CaptionDocument(
        [
            new CaptionCue(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                "text",
                metadata: Metadata(CaptionMetadataKeys.WebVttClasses, "bad class")),
        ]);

        Assert.Multiple(() =>
        {
            Assert.Throws<CaptionExportException>(() =>
                _serializer.Export(invalidLanguage, CaptionFormats.WebVtt));
            Assert.Throws<CaptionExportException>(() =>
                _serializer.Export(invalidClass, CaptionFormats.WebVtt));
        });
    }

    [Test]
    public void ImportAss_DialogueMetadataCommasEscapesAndOverrides_AreHandled()
    {
        const string source = """
            [Script Info]
            ScriptType: v4.00+

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.23,0:00:03.45,Narration,Alice,0,0,0,beutl-language=ja-JP,{\i1}Hello, world\Nnext\hline{\i0}
            """;

        CaptionImportResult result = Import(source, CaptionFormats.Ass);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document![0], Is.EqualTo(new CaptionCue(
                TimeSpan.FromMilliseconds(1230),
                TimeSpan.FromMilliseconds(3450),
                "Hello, world\nnext\u00A0line",
                "Alice",
                "ja-JP",
                Metadata(CaptionMetadataKeys.AssStyle, "Narration"))));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.UnsupportedMarkup
                && diagnostic.LineNumber == 6));
        });
    }

    [TestCase(@"{\an8}Top", "Top")]
    [TestCase(@"{\k20}lyric", "lyric")]
    public void ImportAss_OverrideBlocksProduceAnExplicitDiagnostic(
        string markedUpText,
        string expectedText)
    {
        string source = $"[Events]\nFormat: Start, End, Text\nDialogue: 0:00:00.00,0:00:01.00,{markedUpText}\n";

        CaptionImportResult result = Import(source, CaptionFormats.Ass);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document![0].Text, Is.EqualTo(expectedText));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.UnsupportedMarkup
                && diagnostic.LineNumber == 3));
        }
    }

    [Test]
    public void ImportSsa_ActorAndLegacyFormatting_ArePreservedAsPlainCueData()
    {
        const string source = """
            [Events]
            Format: Marked, Start, End, Style, Actor, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: Marked=0,0:00:00.00,0:00:01.00,Default,Bob,0,0,0,,<b>Hello</b>
            """;

        CaptionImportResult result = Import(source, CaptionFormats.Ass);

        Assert.That(result.Document![0], Is.EqualTo(new CaptionCue(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            "Hello",
            "Bob",
            null,
            Metadata(CaptionMetadataKeys.AssStyle, "Default"))));
    }

    [Test]
    public void AssRoundTrip_PreservesStyleSpeakerLanguageCommaBackslashAndTrailingSpaces()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(2010),
                "Hello, world\nC:\\New  ",
                "Alice",
                "pt-BR",
                Metadata(CaptionMetadataKeys.AssStyle, "Narration")),
        ]);

        byte[] exported = _serializer.Export(document, CaptionFormats.Ass);
        string text = Encoding.UTF8.GetString(exported);
        CaptionImportResult imported = _serializer.Import(exported, CaptionFormats.Ass);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Style: Narration,"));
            Assert.That(text, Does.Contain("Hello, world\\NC:\\\\New  "));
            Assert.That(imported.IsSuccess, Is.True);
            Assert.That(imported.Document!.Cues, Is.EqualTo(document.Cues));
        });
    }

    [Test]
    public void AssRoundTrip_DistinguishesAbsentStyleFromExplicitDefaultStyle()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "plain"),
            new CaptionCue(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                "localized",
                language: "ja-JP"),
            new CaptionCue(
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(3),
                "explicit",
                metadata: Metadata(CaptionMetadataKeys.AssStyle, "Default")),
        ]);

        byte[] exported = _serializer.Export(document, CaptionFormats.Ass);
        string text = Encoding.UTF8.GetString(exported);
        CaptionDocument imported = _serializer.Import(exported, CaptionFormats.Ass).Document!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                text,
                Does.Contain("beutl-language=ja-JP;beutl-style=implicit"));
            Assert.That(imported[0].Metadata.ContainsKey(CaptionMetadataKeys.AssStyle), Is.False);
            Assert.That(imported[0].Language, Is.Null);
            Assert.That(
                imported[1].Metadata.ContainsKey(CaptionMetadataKeys.AssStyle),
                Is.False);
            Assert.That(imported[1].Language, Is.EqualTo("ja-JP"));
            Assert.That(
                imported[2].Metadata.GetValueOrDefault(CaptionMetadataKeys.AssStyle),
                Is.EqualTo("Default"));
            Assert.DoesNotThrow(() => _serializer.Export(
                new CaptionDocument([imported[0]]),
                CaptionFormats.Srt));
        }
    }

    [Test]
    public void BuiltInCodecs_UseIndependentWebVttClassAndAssStyleMetadata()
    {
        CaptionMetadata metadata = CaptionMetadata.Empty
            .Set(CaptionMetadataKeys.WebVttClasses, "web-class")
            .Set(CaptionMetadataKeys.AssStyle, "AssStyle");
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text", metadata: metadata),
        ]);

        string webVtt = Encoding.UTF8.GetString(_serializer.Export(document, CaptionFormats.WebVtt));
        string ass = Encoding.UTF8.GetString(_serializer.Export(document, CaptionFormats.Ass));

        Assert.Multiple(() =>
        {
            Assert.That(webVtt, Does.Contain("<c.web-class>"));
            Assert.That(webVtt, Does.Not.Contain("AssStyle"));
            Assert.That(ass, Does.Contain("Style: AssStyle,"));
            Assert.That(ass, Does.Not.Contain("web-class"));
        });
    }

    [Test]
    public void ExportAss_SubCentisecondCue_ExpandsToRepresentableBoundary()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.FromTicks(1), TimeSpan.FromTicks(2), "short"),
        ]);

        CaptionImportResult imported = _serializer.Import(
            _serializer.Export(document, CaptionFormats.Ass),
            CaptionFormats.Ass);

        Assert.Multiple(() =>
        {
            Assert.That(imported.Document![0].Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(imported.Document[0].End, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
        });
    }

    [TestCase("0:60:00.00", "0:60:01.00")]
    [TestCase("0:00:00.000", "0:00:01.00")]
    [TestCase("0:00:01.00", "0:00:01.00")]
    [TestCase("99999999999999999999:00:00.00", "99999999999999999999:00:01.00")]
    public void ImportAss_MalformedTiming_RejectsWholeDocument(string start, string end)
    {
        string source = $"[Events]\nFormat: Start, End, Text\nDialogue: {start},{end},text\n";

        CaptionImportResult result = Import(source, CaptionFormats.Ass);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Document, Is.Null);
            Assert.That(result.Diagnostics, Has.Some.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidTiming));
        });
    }

    [Test]
    public void ImportAss_MixedValidity_PreservesValidCuesWithDiagnostics()
    {
        const string source = """
            [Events]
            Format: Start, End, Text
            Dialogue: 0:00:00.00,0:00:01.00,first
            Dialogue: invalid,0:00:02.00,skipped
            Dialogue: 0:00:02.00,0:00:03.00,third
            """;

        CaptionImportResult result = Import(source, CaptionFormats.Ass);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Document!.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "first", "third" }));
            Assert.That(result.Diagnostics, Has.One.Matches<CaptionDiagnostic>(diagnostic =>
                diagnostic.Kind == CaptionDiagnosticKinds.InvalidTiming));
        }
    }

    [Test]
    public void ImportAss_MissingEventsOrUnsupportedFormat_ReturnsFailure()
    {
        CaptionImportResult missingEvents = Import(
            "[Script Info]\nScriptType: v4.00+\n",
            CaptionFormats.Ass);
        CaptionImportResult unsupportedFormat = Import(
            "[Events]\nFormat: Start, Text, End\n",
            CaptionFormats.Ass);

        Assert.Multiple(() =>
        {
            Assert.That(missingEvents.Diagnostics, Has.One.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidHeader));
            Assert.That(unsupportedFormat.Diagnostics, Has.One.Matches<CaptionDiagnostic>(error =>
                error.Kind == CaptionDiagnosticKinds.InvalidStructure));
        });
    }

    [Test]
    public void ExportAss_UnrepresentableOverrideOrFieldDelimiter_ThrowsWithCueIndex()
    {
        var braces = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "literal {brace}"),
        ]);
        var speakerComma = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text", "Doe, Jane"),
        ]);
        var legacyFormatting = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "literal < B >bold</b>"),
        ]);

        CaptionExportException? bracesError = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(braces, CaptionFormats.Ass));
        CaptionExportException? speakerError = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(speakerComma, CaptionFormats.Ass));
        CaptionExportException? legacyFormattingError = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(legacyFormatting, CaptionFormats.Ass));

        Assert.Multiple(() =>
        {
            Assert.That(bracesError!.CueIndex, Is.Zero);
            Assert.That(speakerError!.CueIndex, Is.Zero);
            Assert.That(legacyFormattingError!.CueIndex, Is.Zero);
        });
    }

    [Test]
    public void ExportAss_LossySpeakerOrStyleWhitespace_ThrowsWithCueIndex()
    {
        var speakerWhitespace = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.Zero, TimeSpan.FromSeconds(1), "text", " Alice"),
        ]);
        var styleWhitespace = new CaptionDocument(
        [
            new CaptionCue(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                "text",
                metadata: Metadata(CaptionMetadataKeys.AssStyle, "Narration ")),
        ]);

        CaptionExportException? speakerError = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(speakerWhitespace, CaptionFormats.Ass));
        CaptionExportException? styleError = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(styleWhitespace, CaptionFormats.Ass));

        Assert.Multiple(() =>
        {
            Assert.That(speakerError!.CueIndex, Is.Zero);
            Assert.That(styleError!.CueIndex, Is.Zero);
        });
    }

    [Test]
    public void Export_InvalidCueTiming_ThrowsWithCueIndex()
    {
        var document = new CaptionDocument(
        [
            new CaptionCue(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "text"),
        ]);

        CaptionExportException? exception = Assert.Throws<CaptionExportException>(() =>
            _serializer.Export(document, CaptionFormats.WebVtt));

        Assert.That(exception!.CueIndex, Is.Zero);
    }

    private CaptionImportResult Import(string value, CaptionFormatId format)
        => _serializer.Import(new UTF8Encoding(false, true).GetBytes(value), format);

    private static CaptionMetadata Metadata(string key, string value)
        => CaptionMetadata.Empty.Set(key, value);
}
