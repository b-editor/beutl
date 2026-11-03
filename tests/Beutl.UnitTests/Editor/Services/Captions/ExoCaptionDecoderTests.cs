using System.Text;
using Beutl.Editor.Services.Captions;

namespace Beutl.UnitTests.Editor.Services.Captions;

[TestFixture]
public class ExoCaptionDecoderTests
{
    private readonly ExoCaptionDecoder _decoder = new();

    // As tools that write these files lay them out: UTF-16LE in hexadecimal, filled with zeros to 4096 digits.
    private static string Hex(string text) => Convert.ToHexStringLower(Encoding.Unicode.GetBytes(text)).PadRight(4096, '0');

    private static string TextObject(int index, int start, int end, string text, string extra = "") =>
        $"""
        [{index}]
        start={start}
        end={end}
        layer=1
        overlay=1
        camera=0
        {extra}
        [{index}.0]
        _name=テキスト
        サイズ=34
        font=MS UI Gothic
        text={Hex(text)}
        [{index}.1]
        _name=標準描画
        X=0.0
        Y=0.0

        """;

    private static string File(string objects, int rate = 30, int scale = 1) =>
        $"""
        [exedit]
        width=1920
        height=1080
        rate={rate}
        scale={scale}
        length=300
        audio_rate=44100
        audio_ch=2
        {objects}
        """;

    [Test]
    public void A_text_object_becomes_a_cue_at_the_times_of_its_frames()
    {
        CaptionImportResult result = _decoder.Decode(File(TextObject(0, 31, 60, "こんにちは")));

        CaptionCue cue = result.Document!.Cues.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(cue.Text, Is.EqualTo("こんにちは"));
            Assert.That(cue.Start, Is.EqualTo(TimeSpan.FromSeconds(1)), "Frames count from 1.");
            Assert.That(cue.End, Is.EqualTo(TimeSpan.FromSeconds(2)), "The end frame is shown.");
        });
    }

    [Test]
    public void The_frame_rate_is_the_rate_over_the_scale()
    {
        CaptionCue cue = _decoder.Decode(File(TextObject(0, 30001, 60000, "a"), rate: 30000, scale: 1001)).Document!.Cues.Single();

        Assert.Multiple(() =>
        {
            Assert.That(cue.Start, Is.EqualTo(TimeSpan.FromSeconds(1001)));
            Assert.That(cue.End, Is.EqualTo(TimeSpan.FromSeconds(2002)));
        });
    }

    [Test]
    public void Line_breaks_in_the_text_become_plain_ones()
    {
        Assert.That(_decoder.Decode(File(TextObject(0, 1, 30, "一行目\r\n二行目"))).Document![0].Text, Is.EqualTo("一行目\n二行目"));
    }

    [Test]
    public void Other_objects_are_passed_over()
    {
        const string Image = """
            [0]
            start=1
            end=30
            layer=2
            [0.0]
            _name=画像ファイル
            file=C:\a.png

            """;
        // A script has text but no font.
        string script = $"""
            [1]
            start=1
            end=30
            layer=3
            [1.0]
            _name=図形
            type=1
            [1.1]
            _name=スクリプト制御
            text={Hex("obj.alpha = 0")}

            """;

        CaptionImportResult result = _decoder.Decode(File(Image + script + TextObject(2, 1, 30, "字幕")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Document!.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "字幕" }));
            Assert.That(result.Diagnostics, Is.Empty);
        });
    }

    [Test]
    public void Cues_come_in_the_order_of_their_times_whatever_the_layers()
    {
        CaptionImportResult result = _decoder.Decode(File(TextObject(0, 61, 90, "後") + TextObject(1, 1, 30, "先")));

        Assert.That(result.Document!.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "先", "後" }));
    }

    [Test]
    public void A_mid_point_continues_the_object_it_splits()
    {
        string split = TextObject(0, 1, 30, "字幕") + TextObject(1, 31, 60, "字幕", "chain=1");

        CaptionCue cue = _decoder.Decode(File(split)).Document!.Cues.Single();

        Assert.That(cue.End, Is.EqualTo(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void Control_tags_are_left_out_and_reported()
    {
        CaptionImportResult result = _decoder.Decode(File(TextObject(0, 1, 30, "<s60,MS ゴシック,B><#ff0000>赤<#><p10,-5><r5><w*2>字<c2><c><?mes(\"x\")?> <b>")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Document![0].Text, Is.EqualTo("赤字 <b>"), "Only exedit's tags are control text.");
            Assert.That(result.Diagnostics.Select(item => item.Kind), Is.EqualTo(new[] { CaptionDiagnosticKinds.UnsupportedMarkup }));
        });
    }

    [Test]
    public void Text_objects_with_nothing_to_show_are_passed_over()
    {
        CaptionImportResult result = _decoder.Decode(File(TextObject(0, 1, 30, "") + TextObject(1, 1, 30, "<#ff0000>") + TextObject(2, 1, 30, "字幕")));

        Assert.That(result.Document!.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "字幕" }));
    }

    [Test]
    public void An_object_that_cannot_be_read_is_reported_and_the_others_kept()
    {
        string broken = TextObject(0, 1, 30, "a").Replace(Hex("a"), "{text:title}", StringComparison.Ordinal);
        string backwards = TextObject(1, 30, 1, "b");

        CaptionImportResult result = _decoder.Decode(File(broken + backwards + TextObject(2, 1, 30, "字幕")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Document!.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "字幕" }));
            Assert.That(result.Diagnostics.Select(item => item.Kind), Is.EqualTo(new[]
            {
                CaptionDiagnosticKinds.InvalidStructure,
                CaptionDiagnosticKinds.InvalidTiming,
            }));
        });
    }

    [Test]
    public void A_mid_point_that_cannot_be_read_is_reported_and_the_cue_kept()
    {
        string split = TextObject(0, 1, 30, "字幕") + TextObject(1, 60, 31, "字幕", "chain=1");

        CaptionImportResult result = _decoder.Decode(File(split));

        Assert.Multiple(() =>
        {
            Assert.That(result.Document!.Cues.Single().End, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(result.Diagnostics.Select(item => item.Kind), Is.EqualTo(new[] { CaptionDiagnosticKinds.InvalidTiming }));
        });
    }

    // Searching for the end of a script tag is quadratic in the text, which exedit never makes longer than this.
    [Test]
    public void Text_longer_than_exedit_writes_is_reported_and_not_searched()
    {
        string longer = TextObject(0, 1, 30, "a").Replace(Hex("a"), Convert.ToHexStringLower(Encoding.Unicode.GetBytes(string.Concat(Enumerable.Repeat("<?", 2049)))), StringComparison.Ordinal);

        CaptionImportResult result = _decoder.Decode(File(longer + TextObject(1, 31, 60, "字幕")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Document!.Cues.Select(cue => cue.Text), Is.EqualTo(new[] { "字幕" }));
            Assert.That(result.Diagnostics.Select(item => item.Kind), Is.EqualTo(new[] { CaptionDiagnosticKinds.InvalidStructure }));
        });
    }

    [Test]
    public void A_file_of_nothing_but_unreadable_objects_fails()
    {
        string broken = TextObject(0, 1, 30, "a").Replace(Hex("a"), "zz", StringComparison.Ordinal);

        Assert.That(_decoder.Decode(File(broken)).IsSuccess, Is.False);
    }

    [TestCase("rate=0")]
    [TestCase("")]
    public void A_file_without_a_frame_rate_fails(string rate)
    {
        string file = File(TextObject(0, 1, 30, "a")).Replace("rate=30", rate, StringComparison.Ordinal);

        CaptionImportResult result = _decoder.Decode(file);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Diagnostics.Single().Kind, Is.EqualTo(CaptionDiagnosticKinds.InvalidHeader));
        });
    }

    [Test]
    public void The_format_is_only_read()
    {
        ICaptionCodecProvider codecs = CaptionCatalog.ComposeWithDefaultElementFactory(
            "Default", [], new DefaultTextCaptionElementFactory(Beutl.Media.FontFamily.Default)).Codecs;

        Assert.That(codecs.TryGetByFileName("subtitles.EXO", out CaptionCodecInfo? codec), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(codec!.Format, Is.EqualTo(CaptionFormats.Exo));
            Assert.That(codec.CanDecode, Is.True);
            Assert.That(codec.CanEncode, Is.False);
        });
    }
}
