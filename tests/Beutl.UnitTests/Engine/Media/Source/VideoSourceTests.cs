using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Moq;

namespace Beutl.UnitTests.Engine.Media.Source;

[TestFixture]
[NonParallelizable]
public class VideoSourceTests
{
    private readonly List<Mock<MediaReader>> _readers = [];
    private Mock<IDecoderInfo> _decoder = null!;
    private string _root = null!;
    private string _path = null!;
    private bool _hasVideo;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"beutl_video_source_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "audio-only.test-no-video");
        File.WriteAllBytes(_path, []);
        _hasVideo = false;
        _readers.Clear();
        _decoder = new Mock<IDecoderInfo>();
        _decoder.Setup(x => x.IsSupported(It.IsAny<string>())).Returns((string file) => file == _path);
        _decoder.Setup(x => x.Open(_path, It.Is<MediaOptions>(x => x.StreamsToLoad == MediaMode.Video)))
            .Returns(() => CreateReader(_hasVideo).Object);
        DecoderRegistry.Register(_decoder.Object);
    }

    [TearDown]
    public void TearDown()
    {
        DecoderRegistry.Unregister(_decoder.Object);
        foreach (Mock<MediaReader> reader in _readers)
            reader.Object.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AudioOnlyMedia_YieldsOfflinePlaceholderAndReleasesReader(bool disableResourceShare)
    {
        VideoSource source = CreateSource();
        var context = new CompositionContext(MediaTime.Zero) { DisableResourceShare = disableResourceShare };
        using var resource = source.ToResource(context);

        AssertOffline(resource);
        Assert.That(_readers.Single().Object.IsDisposed, Is.True);
        _readers.Single().VerifyGet(x => x.VideoInfo, Times.Never);

        using var second = source.ToResource(context);
        AssertOffline(second);
        Assert.That(_readers, Has.Count.EqualTo(2), "A rejected reader must not be shared with a new resource.");
        Assert.That(_readers.All(x => x.Object.IsDisposed), Is.True);
    }

    [Test]
    public void UnchangedAudioOnlySource_DoesNotReopenOnReconcile()
    {
        VideoSource source = CreateSource();
        using var resource = source.ToResource(CompositionContext.Default);
        int version = resource.Version;
        bool versionBumped = false;

        resource.Reconcile(source, CompositionContext.Default, ref versionBumped);

        AssertOffline(resource);
        Assert.That(_readers, Has.Count.EqualTo(1));
        Assert.That(resource.Version, Is.EqualTo(version));
    }

    [Test]
    public void SharedReaderWithoutVideo_YieldsOfflinePlaceholderAndReleasesItsReference()
    {
        _hasVideo = true;
        VideoSource source = CreateSource();
        using var first = source.ToResource(CompositionContext.Default);
        Mock<MediaReader> reader = _readers.Single();
        reader.SetupGet(x => x.HasVideo).Returns(false);
        reader.SetupGet(x => x.VideoInfo).Throws(new InvalidOperationException("The stream does not exist."));
        reader.Invocations.Clear();

        using var second = source.ToResource(CompositionContext.Default);

        AssertOffline(second);
        Assert.That(_readers, Has.Count.EqualTo(1), "The shared-reader path must check HasVideo too.");
        reader.VerifyGet(x => x.VideoInfo, Times.Never);
        Assert.That(reader.Object.IsDisposed, Is.False, "The first resource still owns the reader.");
        first.Dispose();
        Assert.That(reader.Object.IsDisposed, Is.True, "The offline resource must have released its reference.");
    }

    [Test]
    public void Reload_AfterVideoTrackChanges_UpdatesOfflineStateAndMetadata()
    {
        _hasVideo = true;
        VideoSource source = CreateSource();
        using var resource = source.ToResource(CompositionContext.Default);
        Assert.That(resource.IsOffline, Is.False);
        Assert.That(resource.Duration, Is.EqualTo(MediaTime.FromSeconds(2)));

        _hasVideo = false;
        source.InvalidateResourceCache();
        bool versionBumped = false;
        resource.Reconcile(source, CompositionContext.Default, ref versionBumped);

        AssertOffline(resource);
        Assert.That(_readers.All(x => x.Object.IsDisposed), Is.True);

        _hasVideo = true;
        source.InvalidateResourceCache();
        versionBumped = false;
        resource.Reconcile(source, CompositionContext.Default, ref versionBumped);

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsOffline, Is.False);
            Assert.That(resource.MediaReader, Is.SameAs(_readers.Last().Object));
            Assert.That(resource.Duration, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(80, 60)));
            Assert.That(resource.FrameRate, Is.EqualTo(new Rational(24, 1)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SceneWithAudioOnlyVideoClip_ComposesWithoutReopeningTheReader(bool disableResourceShare)
    {
        var video = new SourceVideo();
        video.Source.Value = CreateSource();
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(_root, "clip.belm")),
        };
        element.AddObject(video);
        var scene = new Scene(320, 180, string.Empty) { Uri = new Uri(Path.Combine(_root, "test.scene")) };
        var application = new BeutlApplication { Project = { Value = new Project() } };
        application.Project.Value!.Items.Add(scene);
        scene.Children.Add(element);
        using var compositor = new SceneCompositor(scene)
        {
            DisableResourceShare = disableResourceShare,
            ForceOriginalSource = true,
        };

        CompositionFrame first = compositor.EvaluateGraphics(MediaTime.Zero);
        CompositionFrame next = compositor.EvaluateGraphics(MediaTime.FromSeconds(0.5));

        var resource = (SourceVideo.Resource)first.Objects.Single();
        AssertOffline(resource.Source!);
        Assert.That(next.Objects.Single(), Is.SameAs(resource));
        Assert.That(_readers, Has.Count.EqualTo(1));
        Assert.That(video.TryGetOriginalDuration(out MediaTime duration), Is.False);
        Assert.That(duration, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase(48, 2)]
    [TestCase(48, 3)] // A longer audio track must not extend the video frame interval.
    [TestCase(0, 2)] // Containers without a frame count use the duration instead.
    public void FrameSelection_UsesTheVideoEndWithoutExtendingPastIt(long frameCount, int durationSeconds)
    {
        Mock<MediaReader> reader = CreateReader(true);
        reader.SetupGet(x => x.VideoInfo).Returns(
            new VideoStreamInfo("test", frameCount, new PixelSize(80, 60), new Rational(24))
            {
                Duration = new Rational(durationSeconds),
            });
        _decoder.Setup(x => x.Open(_path, It.IsAny<MediaOptions>())).Returns(reader.Object);
        using var resource = CreateSource().ToResource(CompositionContext.Default);

        MediaTime end = MediaTime.FromSeconds(2);
        Assert.Multiple(() =>
        {
            Assert.That(resource.GetFrameIndex(end - new MediaTime(1)), Is.EqualTo(47));
            Assert.That(resource.GetFrameIndex(end), Is.EqualTo(48));
            Assert.That(resource.GetFrameIndex(end + MediaTime.FromFrames(1, new Rational(24))), Is.EqualTo(49));
            Assert.That(resource.GetFrameIndex(MediaTime.Zero), Is.Zero);
            Assert.That(resource.GetFrameIndex(MediaTime.FromFrames(3, new Rational(48))), Is.EqualTo(2),
                "Nearest-frame sampling inside the clip must stay unchanged.");
        });
    }

    private VideoSource CreateSource()
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(_path));
        return source;
    }

    private Mock<MediaReader> CreateReader(bool hasVideo)
    {
        var reader = new Mock<MediaReader>();
        reader.SetupGet(x => x.HasVideo).Returns(hasVideo);
        reader.SetupGet(x => x.HasAudio).Returns(true);
        reader.SetupGet(x => x.ProxyResolution).Returns((ProxyResolution?)null);
        if (hasVideo)
            reader.SetupGet(x => x.VideoInfo).Returns(new VideoStreamInfo("test", 48L, new PixelSize(80, 60), new Rational(24, 1)));
        else
            reader.SetupGet(x => x.VideoInfo).Throws(new InvalidOperationException("The stream does not exist."));
        _readers.Add(reader);
        return reader;
    }

    private static void AssertOffline(VideoSource.Resource resource)
    {
        Assert.Multiple(() =>
        {
            Assert.That(resource.IsOffline, Is.True);
            Assert.That(resource.MediaReader, Is.Null);
            Assert.That(resource.Duration, Is.EqualTo(MediaTime.Zero));
            Assert.That(resource.FrameRate, Is.EqualTo(new Rational(30, 1)));
            Assert.That(resource.FrameSize, Is.EqualTo(OfflineMediaPlaceholder.Size));
            Assert.That(resource.LogicalFrameSize, Is.EqualTo(OfflineMediaPlaceholder.Size));
            Assert.That(resource.ProxyResolution, Is.Null);
        });
        Assert.That(resource.Read(0, out Ref<Bitmap>? bitmap), Is.True);
        using (bitmap)
            Assert.That(new PixelSize(bitmap!.Value.Width, bitmap.Value.Height), Is.EqualTo(OfflineMediaPlaceholder.Size));
        Assert.That(resource.Read(10, out bitmap), Is.True);
        bitmap?.Dispose();
    }
}
