using Beutl.Extensions.FFmpeg.PropertyEditors;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class CodecSearchQueryTests
{
    [TestCase("nvenc h264", true)]
    [TestCase("h264 nvidia", true)]
    [TestCase("nvenc hevc", false)]
    public void Every_word_has_to_occur_in_the_name_or_the_long_name(string query, bool matches)
    {
        var key = new CodecSearchKey("h264_nvenc", "NVIDIA NVENC H.264 encoder");

        Assert.That(CodecSearchQuery.Parse(query).Score(key).HasValue, Is.EqualTo(matches));
    }

    [TestCase("h264", "libx264", "libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10")]
    [TestCase("mpeg4 part10", "libx264", "libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10")]
    [TestCase("proresks", "prores_ks", "Apple ProRes (iCodec Pro)")]
    [TestCase("ＡＡＣ", "aac", "AAC (Advanced Audio Coding)")]
    public void Case_width_and_punctuation_do_not_matter(string query, string name, string longName)
    {
        Assert.That(CodecSearchQuery.Parse(query).Score(new CodecSearchKey(name, longName)).HasValue, Is.True);
    }

    [Test]
    public void The_name_ranks_above_its_start_which_ranks_above_the_start_of_the_long_name()
    {
        CodecSearchQuery query = CodecSearchQuery.Parse("aac");

        int? name = query.Score(new CodecSearchKey("aac", "AAC (Advanced Audio Coding)"));
        int? namePrefix = query.Score(new CodecSearchKey("aac_at", "aac (AudioToolbox)"));
        int? longNamePrefix = query.Score(new CodecSearchKey("libfdk_aac", "AAC-LC by Fraunhofer FDK"));
        int? anywhere = query.Score(new CodecSearchKey("libfdk_aac", "Fraunhofer FDK AAC"));

        Assert.Multiple(() =>
        {
            Assert.That(name, Is.GreaterThan(namePrefix));
            Assert.That(namePrefix, Is.GreaterThan(longNamePrefix));
            Assert.That(longNamePrefix, Is.GreaterThan(anywhere));
            Assert.That(anywhere, Is.Not.Null);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(" / - ")]
    public void A_query_without_letters_or_digits_searches_nothing(string? query)
    {
        Assert.That(CodecSearchQuery.Parse(query).IsEmpty, Is.True);
    }
}
