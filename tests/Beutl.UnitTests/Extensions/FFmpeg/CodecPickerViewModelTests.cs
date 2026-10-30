using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.PropertyEditors;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class CodecPickerViewModelTests
{
    private static readonly string[] s_keys =
        [CodecPickerViewModel.VideoCodecPinnedItemsKey, CodecPickerViewModel.AudioCodecPinnedItemsKey];

    private static readonly CodecRecord[] s_codecs =
    [
        CodecRecord.Default,
        new("mpeg4", "MPEG-4 part 2"),
        new("libx264", "libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10"),
        new("h264_videotoolbox", "VideoToolbox H.264 Encoder"),
        new("prores_ks", "Apple ProRes (iCodec Pro)"),
    ];

    private readonly Dictionary<string, string> _pinnedItems = [];

    [SetUp]
    public void ClearPinnedCodecs()
    {
        foreach (string key in s_keys)
        {
            _pinnedItems[key] = Preferences.Default.Get(key, "[]");
            Preferences.Default.Set(key, "[]");
        }
    }

    [TearDown]
    public void RestorePinnedCodecs()
    {
        foreach ((string key, string value) in _pinnedItems)
        {
            Preferences.Default.Set(key, value);
        }
    }

    [Test]
    public void Codecs_show_their_long_names_with_their_names_below_in_the_order_given()
    {
        var viewModel = new CodecPickerViewModel(s_codecs, CodecPickerViewModel.VideoCodecPinnedItemsKey);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Items.Select(i => i.DisplayName), Is.EqualTo(s_codecs.Select(c => c.LongName)));
            Assert.That(viewModel.Items.Select(i => i.UserData), Is.EqualTo(s_codecs));
            Assert.That(viewModel.Items[2].Description, Is.EqualTo("libx264"));
            // The default codec has only one name to show.
            Assert.That(viewModel.Items[0].Description, Is.Null);
        });
    }

    [Test]
    public void A_search_finds_codecs_by_either_name_and_highlights_the_best_match()
    {
        var viewModel = new CodecPickerViewModel(s_codecs, CodecPickerViewModel.VideoCodecPinnedItemsKey);

        viewModel.SearchText.Value = "h264";

        Assert.Multiple(() =>
        {
            Assert.That(Names(viewModel), Is.EqualTo(new[] { "h264_videotoolbox", "libx264" }));
            Assert.That(Codec(viewModel.SelectedItem.Value)?.Name, Is.EqualTo("h264_videotoolbox"));
        });

        viewModel.SearchText.Value = "prores";

        Assert.That(Names(viewModel), Is.EqualTo(new[] { "prores_ks" }));
    }

    [Test]
    public void Pins_come_first_and_last_until_the_next_picker()
    {
        var viewModel = new CodecPickerViewModel(s_codecs, CodecPickerViewModel.VideoCodecPinnedItemsKey);
        viewModel.Pin(viewModel.Items.Single(i => Codec(i)?.Name == "prores_ks"));

        var reopened = new CodecPickerViewModel(s_codecs, CodecPickerViewModel.VideoCodecPinnedItemsKey);

        Assert.Multiple(() =>
        {
            Assert.That(Names(viewModel)[0], Is.EqualTo("prores_ks"));
            Assert.That(Names(reopened)[0], Is.EqualTo("prores_ks"));
            Assert.That(reopened.Items[0].IsPinned, Is.True);
        });

        reopened.Unpin(reopened.Items[0]);

        Assert.That(Names(new CodecPickerViewModel(s_codecs, CodecPickerViewModel.VideoCodecPinnedItemsKey)),
            Is.EqualTo(s_codecs.Select(c => c.Name)));
    }

    [Test]
    public void Video_and_audio_codecs_are_pinned_apart_from_each_other()
    {
        var video = new CodecPickerViewModel(s_codecs, CodecPickerViewModel.VideoCodecPinnedItemsKey);
        video.Pin(video.Items.Single(i => Codec(i)?.Name == "mpeg4"));

        var audio = new CodecPickerViewModel(s_codecs, CodecPickerViewModel.AudioCodecPinnedItemsKey);

        Assert.That(audio.Items.Any(i => i.IsPinned), Is.False);
    }

    private static CodecRecord? Codec(PinnableLibraryItem? item) => item?.UserData as CodecRecord;

    private static string[] Names(CodecPickerViewModel viewModel)
    {
        return [.. viewModel.Items.Select(i => Codec(i)!.Name)];
    }
}
