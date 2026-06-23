using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.PropertyEditors;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class CodecOptionQueryTests
{
    [Test]
    public void Create_DefaultCodec_MapsToNullName()
    {
        CodecQueryParams query = CodecOptionQuery.Create(CodecRecord.Default, "out.mp4");

        Assert.That(query.CodecName, Is.Null);
        Assert.That(query.OutputFile, Is.EqualTo("out.mp4"));
    }

    [Test]
    public void Create_NamedCodec_KeepsName()
    {
        CodecQueryParams query = CodecOptionQuery.Create(new CodecRecord("aac", "AAC"), "out.mp4");

        Assert.That(query.CodecName, Is.EqualTo("aac"));
        Assert.That(query.OutputFile, Is.EqualTo("out.mp4"));
    }

    [Test]
    public void BuildCacheKey_NullCodec_UsesDefaultSentinelAndNulDelimiter()
    {
        string key = CodecOptionQuery.BuildCacheKey(new CodecQueryParams(null, "out.mp4"));

        Assert.That(key, Is.EqualTo("<default>\0out.mp4"));
    }

    [Test]
    public void BuildCacheKey_NamedCodec_UsesNameAndNulDelimiter()
    {
        string key = CodecOptionQuery.BuildCacheKey(new CodecQueryParams("aac", "out.mp4"));

        Assert.That(key, Is.EqualTo("aac\0out.mp4"));
    }

    // The cache key and the worker request both derive from one CodecQueryParams, so equal snapshots
    // must produce one key and any difference must produce a distinct one.
    [Test]
    public void BuildCacheKey_EqualSnapshots_ProduceEqualKeys()
    {
        var a = CodecOptionQuery.Create(new CodecRecord("aac", "AAC"), "out.mp4");
        var b = CodecOptionQuery.Create(new CodecRecord("aac", "different long name"), "out.mp4");

        Assert.That(a, Is.EqualTo(b));
        Assert.That(CodecOptionQuery.BuildCacheKey(a), Is.EqualTo(CodecOptionQuery.BuildCacheKey(b)));
    }

    [Test]
    public void BuildCacheKey_DifferentCodecOrFile_ProduceDistinctKeys()
    {
        string aac = CodecOptionQuery.BuildCacheKey(new CodecQueryParams("aac", "out.mp4"));
        string mp3 = CodecOptionQuery.BuildCacheKey(new CodecQueryParams("mp3", "out.mp4"));
        string otherFile = CodecOptionQuery.BuildCacheKey(new CodecQueryParams("aac", "other.mp4"));

        Assert.That(aac, Is.Not.EqualTo(mp3));
        Assert.That(aac, Is.Not.EqualTo(otherFile));
    }

    [Test]
    public void BuildCacheKey_NullVsEmptyOutputFile_ProduceDistinctKeys()
    {
        string nullFile = CodecOptionQuery.BuildCacheKey(new CodecQueryParams("aac", null));
        string emptyFile = CodecOptionQuery.BuildCacheKey(new CodecQueryParams("aac", ""));

        Assert.That(nullFile, Is.Not.EqualTo(emptyFile));
    }
}
