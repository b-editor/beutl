using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class MediaSourceResourceShareTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    // How to make a source of one media kind that has read a file, the object its resources share (a MediaReader,
    // or a Bitmap for images), and a second file it can read.
    public sealed record SharedMedia(
        Func<MediaSource> CreateSource,
        Func<EngineObject.Resource, object?> Shared,
        Func<Uri> CreateOtherFile,
        Func<EngineObject.Resource, PixelSize>? FrameSize = null,
        PixelSize OtherFrameSize = default);

    private static IEnumerable<TestCaseData> MediaKinds()
    {
        yield return new TestCaseData(new SharedMedia(
                static () => ReadFrom(new VideoSource(), CreateVideoFile(80)),
                static resource => Underlying(((VideoSource.Resource)resource).MediaReader),
                static () => CreateVideoFile(120),
                static resource => ((VideoSource.Resource)resource).MediaReader!.VideoInfo.FrameSize,
                new PixelSize(120, 120)))
            .SetArgDisplayNames(nameof(VideoSource));
        yield return new TestCaseData(new SharedMedia(
                static () => ReadFrom(new SoundSource(), new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 2.0))),
                static resource => Underlying(((SoundSource.Resource)resource).MediaReader),
                static () => new Uri(TestMediaHelper.CreateTestAudioFile(sampleRate: 48000, durationSeconds: 3.0))))
            .SetArgDisplayNames(nameof(SoundSource));
        yield return new TestCaseData(new SharedMedia(
                static () => ReadFrom(new ImageSource(), TestMediaHelper.CreateTestImageUri(16, 16, Colors.Red)),
                static resource => ((ImageSource.Resource)resource).Bitmap,
                static () => TestMediaHelper.CreateTestImageUri(32, 32, Colors.Blue),
                static resource => ((ImageSource.Resource)resource).FrameSize,
                new PixelSize(32, 32)))
            .SetArgDisplayNames(nameof(ImageSource));

        // Each resource reads through its own handle; sharing means the handles read the same reader.
        static object? Underlying(MediaReader? reader) => reader is null ? null : SharedMediaReaders.UnderlyingOf(reader);

        static Uri CreateVideoFile(int size)
            => new(TestMediaHelper.CreateTestVideoFile(size, size, new Rational(30, 1), 60));

        static MediaSource ReadFrom(MediaSource source, Uri uri)
        {
            source.ReadFrom(uri);
            return source;
        }
    }

    private static CompositionContext EncodeContext() => new(MediaTime.Zero) { DisableResourceShare = true };

    [TestCaseSource(nameof(MediaKinds))]
    public void SharedByDefault_ReusesTheSharedObject(SharedMedia media)
    {
        MediaSource source = media.CreateSource();

        using EngineObject.Resource a = source.ToResource(CompositionContext.Default);
        using EngineObject.Resource b = source.ToResource(CompositionContext.Default);

        Assert.That(media.Shared(a), Is.Not.Null);
        Assert.That(media.Shared(b), Is.Not.Null);
        Assert.That(media.Shared(b), Is.SameAs(media.Shared(a)),
            "DisableResourceShare=false では同じものを共有するはず");
    }

    [TestCaseSource(nameof(MediaKinds))]
    public void DisableResourceShare_YieldsAnIndependentObject(SharedMedia media)
    {
        MediaSource source = media.CreateSource();

        using EngineObject.Resource preview = source.ToResource(CompositionContext.Default);
        using EngineObject.Resource encode = source.ToResource(EncodeContext());

        Assert.That(media.Shared(preview), Is.Not.Null);
        Assert.That(media.Shared(encode), Is.Not.Null);
        Assert.That(media.Shared(encode), Is.Not.SameAs(media.Shared(preview)),
            "DisableResourceShare=true では専用のものが割り当てられるはず");
    }

    [TestCaseSource(nameof(MediaKinds))]
    public void DisableResourceShare_DoesNotContaminateSharedCache(SharedMedia media)
    {
        MediaSource source = media.CreateSource();

        // エンコード側が先に Resource を生成しても、プレビュー側 (共有モード) は
        // エンコード専用のものを掴まない
        using EngineObject.Resource encode = source.ToResource(EncodeContext());
        using EngineObject.Resource preview = source.ToResource(CompositionContext.Default);

        Assert.That(media.Shared(preview), Is.Not.SameAs(media.Shared(encode)),
            "エンコード専用のものがプレビュー側に漏れてはならない");
    }

    [TestCaseSource(nameof(MediaKinds))]
    public void ReadFromDifferentUri_DoesNotShareStaleCounter(SharedMedia media)
    {
        // 別 Resource が古い URI の Counter を握ったまま ReadFrom(newUri) → ToResource すると、
        // 共有の参照を破棄しないと TryAddRef が成功し新 URI でも古いものが返ってしまう。
        MediaSource source = media.CreateSource();
        Uri newUri = media.CreateOtherFile();

        using EngineObject.Resource oldResource = source.ToResource(CompositionContext.Default);
        Assert.That(media.Shared(oldResource), Is.Not.Null);

        source.ReadFrom(newUri);
        using EngineObject.Resource newResource = source.ToResource(CompositionContext.Default);

        Assert.That(media.Shared(newResource), Is.Not.Null);
        Assert.That(media.Shared(newResource), Is.Not.SameAs(media.Shared(oldResource)),
            "URI 切替後の Resource が旧 URI のものを共有してはならない");
        if (media.FrameSize is { } frameSize)
        {
            Assert.That(frameSize(newResource), Is.EqualTo(media.OtherFrameSize),
                "新 URI に対応するものがロードされるはず");
        }
    }

    // Regression for #2183: a video-only file (no audio track) must not crash SoundSource.Resource.
    // The test decoder's TestMediaReader reports HasAudio == false for .testvideo files, mirroring the
    // FFmpegReaderProxy behavior that threw "The stream does not exist." when AudioInfo was dereferenced.
    [Test]
    public void SoundSource_VideoOnlyFile_DoesNotThrowAndYieldsUnloadedResource()
    {
        var videoPath = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(videoPath));

        using var resource = soundSource.ToResource(CompositionContext.Default);

        Assert.That(resource.MediaReader, Is.Null,
            "音声トラックのないメディアは MediaReader を保持してはならない (#2183)");
        Assert.That(resource.Duration, Is.EqualTo(MediaTime.Zero));
        Assert.That(resource.SampleRate, Is.Zero);
        Assert.That(resource.NumChannels, Is.Zero);
    }

    [Test]
    public void SoundSource_VideoOnlyFile_RepeatedResource_DoesNotThrow()
    {
        var videoPath = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(videoPath));

        using var a = soundSource.ToResource(CompositionContext.Default);
        using var b = soundSource.ToResource(CompositionContext.Default);

        // The unloaded result must be stable: opening the same video-only source again must not throw
        // or resurrect a stale counter.
        Assert.That(a.MediaReader, Is.Null);
        Assert.That(b.MediaReader, Is.Null);
    }
}
