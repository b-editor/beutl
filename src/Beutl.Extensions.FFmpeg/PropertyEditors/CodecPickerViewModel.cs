using Beutl.Controls.PropertyEditors;
using Beutl.Extensions.FFmpeg.Encoding;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

// The codecs an encoder offers, listed by their long names with their names below.
internal sealed class CodecPickerViewModel(IEnumerable<CodecRecord> codecs, string pinnedItemsKey)
    // A few hundred codecs filter fast enough to search on every keystroke.
    : SearchablePickerViewModel<CodecSearchKey>(codecs.Select(CreateEntry), pinnedItemsKey, TimeSpan.Zero)
{
    // Video and audio codecs are pinned separately, as they are listed separately.
    public const string VideoCodecPinnedItemsKey = "FFmpegEncoder.VideoCodec.PinnedItems";
    public const string AudioCodecPinnedItemsKey = "FFmpegEncoder.AudioCodec.PinnedItems";

    protected override Func<CodecSearchKey, int?>? Search(string searchText)
    {
        var query = CodecSearchQuery.Parse(searchText);
        return query.IsEmpty ? null : query.Score;
    }

    private static SearchablePickerEntry<CodecSearchKey> CreateEntry(CodecRecord codec)
    {
        return new SearchablePickerEntry<CodecSearchKey>(
            codec,
            codec.LongName,
            codec.Name,
            new CodecSearchKey(codec.Name, codec.LongName),
            // The default codec has one name, which it shows already.
            codec.Name == codec.LongName ? null : codec.Name);
    }
}
