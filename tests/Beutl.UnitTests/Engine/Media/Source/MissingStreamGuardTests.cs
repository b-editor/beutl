using System.Diagnostics.CodeAnalysis;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Media.Source;

/// <summary>
/// Regression tests for the "The stream does not exist." bug (Project #9 item 213326624).
/// When a media file is opened in Video mode but has no video stream (or Audio mode but
/// has no audio stream), VideoSource.Resource / SoundSource.Resource previously accessed
/// MediaReader.VideoInfo / AudioInfo without a HasVideo / HasAudio guard, throwing a
/// generic Exception that crashed the timeline clip operation.
/// </summary>
[TestFixture]
public class MissingStreamGuardTests
{
    private static bool s_decoderRegistered;
    private readonly List<string> _tempFiles = [];

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (!s_decoderRegistered)
        {
            DecoderRegistry.Register(new MissingStreamDecoderInfo());
            s_decoderRegistered = true;
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string path in _tempFiles)
        {
            File.Delete(path);
        }

        _tempFiles.Clear();
    }

    /// <summary>
    /// A MediaReader whose stream-info accessors throw for absent streams, mirroring
    /// FFmpegReaderProxy — the behavior the missing-stream guards must survive.
    /// </summary>
    private sealed class StubMediaReader(bool hasVideo, bool hasAudio) : MediaReader
    {
        private readonly VideoStreamInfo _videoInfo = new(
            "test", 60, new PixelSize(80, 80), new Rational(30, 1));

        private readonly AudioStreamInfo _audioInfo = new(
            "test", new Rational(2, 1), 44100, 2);

        public override VideoStreamInfo VideoInfo => hasVideo
            ? _videoInfo
            : throw new InvalidOperationException("The video stream does not exist.");

        public override AudioStreamInfo AudioInfo => hasAudio
            ? _audioInfo
            : throw new InvalidOperationException("The audio stream does not exist.");

        public override bool HasVideo => hasVideo;
        public override bool HasAudio => hasAudio;

        protected override bool ReadVideoCore(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        protected override bool ReadAudioCore(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            sound = null;
            return false;
        }

        protected override void Dispose(bool disposing) { }
    }

    private sealed class MissingStreamDecoderInfo : IDecoderInfo
    {
        public string Name => "Missing Stream Test Decoder";

        public MediaReader? Open(string file, MediaOptions options)
        {
            if (file.EndsWith(".no-stream", StringComparison.OrdinalIgnoreCase))
                return new StubMediaReader(hasVideo: false, hasAudio: false);
            if (file.EndsWith(".video-only", StringComparison.OrdinalIgnoreCase))
                return new StubMediaReader(hasVideo: true, hasAudio: false);
            if (file.EndsWith(".audio-only", StringComparison.OrdinalIgnoreCase))
                return new StubMediaReader(hasVideo: false, hasAudio: true);
            return null;
        }

        public bool IsSupported(string file)
        {
            return file.EndsWith(".no-stream", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".video-only", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".audio-only", StringComparison.OrdinalIgnoreCase);
        }

        public IEnumerable<string> VideoExtensions() => [".video-only"];
        public IEnumerable<string> AudioExtensions() => [".audio-only"];
    }

    private string CreateTempFile(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-stream-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, []);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// VideoSource with a file that has no video stream should not throw when
    /// ToResource is called — it should gracefully set default values.
    /// </summary>
    [Test]
    public void VideoSource_NoVideoStream_DoesNotThrow()
    {
        string path = CreateTempFile(".no-stream");
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(path));

        using var resource = videoSource.ToResource(CompositionContext.Default);

        // MediaReader should be non-null (the file opened successfully),
        // but HasVideo is false, so the resource should have default values.
        Assert.That(resource.MediaReader, Is.Not.Null);
        Assert.That(resource.MediaReader!.HasVideo, Is.False);
        Assert.That(resource.Duration, Is.EqualTo(TimeSpan.Zero));
        Assert.That(resource.FrameRate, Is.EqualTo(new Rational(0, 1)));
    }

    /// <summary>
    /// SoundSource with a file that has no audio stream should not throw when
    /// ToResource is called — it should gracefully set default values.
    /// </summary>
    [Test]
    public void SoundSource_NoAudioStream_DoesNotThrow()
    {
        string path = CreateTempFile(".no-stream");
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));

        using var resource = soundSource.ToResource(CompositionContext.Default);

        Assert.That(resource.MediaReader, Is.Not.Null);
        Assert.That(resource.MediaReader!.HasAudio, Is.False);
        Assert.That(resource.Duration, Is.EqualTo(TimeSpan.Zero));
        Assert.That(resource.SampleRate, Is.EqualTo(0));
        Assert.That(resource.NumChannels, Is.EqualTo(0));
    }

    /// <summary>
    /// SoundSource with a video-only file (HasAudio=false) should not throw
    /// when ToResource is called.
    /// </summary>
    [Test]
    public void SoundSource_VideoOnlyFile_DoesNotThrow()
    {
        string path = CreateTempFile(".video-only");
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));

        using var resource = soundSource.ToResource(CompositionContext.Default);

        Assert.That(resource.MediaReader, Is.Not.Null);
        Assert.That(resource.MediaReader!.HasAudio, Is.False);
        Assert.That(resource.SampleRate, Is.EqualTo(0));
        Assert.That(resource.NumChannels, Is.EqualTo(0));
    }

    /// <summary>
    /// VideoSource with an audio-only file (HasVideo=false) should not throw
    /// when ToResource is called.
    /// </summary>
    [Test]
    public void VideoSource_AudioOnlyFile_DoesNotThrow()
    {
        string path = CreateTempFile(".audio-only");
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(path));

        using var resource = videoSource.ToResource(CompositionContext.Default);

        Assert.That(resource.MediaReader, Is.Not.Null);
        Assert.That(resource.MediaReader!.HasVideo, Is.False);
        Assert.That(resource.Duration, Is.EqualTo(TimeSpan.Zero));
    }

    /// <summary>
    /// ReadAudio on a reader with no audio stream should return false
    /// instead of throwing "The stream does not exist."
    /// </summary>
    [Test]
    public void ReadAudio_NoAudioStream_ReturnsFalse()
    {
        string path = CreateTempFile(".video-only");
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));

        using var resource = soundSource.ToResource(CompositionContext.Default);

        // Even if the reader exists, Read should return false when there's no audio stream.
        bool result = resource.Read(0, 100, out _);
        Assert.That(result, Is.False);
    }

    /// <summary>
    /// ReadVideo on a reader with no video stream should return false; the base-class
    /// HasVideo guard must short-circuit before the implementation runs.
    /// </summary>
    [Test]
    public void ReadVideo_NoVideoStream_ReturnsFalse()
    {
        string path = CreateTempFile(".audio-only");
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(path));

        using var resource = videoSource.ToResource(CompositionContext.Default);

        bool result = resource.Read(0, out _);
        Assert.That(result, Is.False);
    }

    /// <summary>
    /// A SoundSource whose file has no audio stream (SampleRate == 0) must compose as
    /// silence. Previously Sound.Compose built a resample graph with SourceSampleRate = 0,
    /// which propagated a 0 Hz AudioProcessContext into SourceNode and crashed AudioBuffer
    /// allocation with ArgumentOutOfRangeException at render time.
    /// </summary>
    [Test]
    public void SourceSound_NoAudioStream_ComposesAsSilence()
    {
        string path = CreateTempFile(".no-stream");
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));

        var sound = new SourceSound
        {
            Source = { CurrentValue = soundSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
        };

        using var resource = (SourceSound.Resource)sound.ToResource(CompositionContext.Default);
        using var context = new AudioContext(44100, 2);

        sound.Compose(context, resource);

        var processContext = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 44100, new AnimationSampler(), null);
        foreach (AudioNode outputNode in context.GetOutputNodes())
        {
            using AudioBuffer buffer = outputNode.Process(processContext);
            for (int ch = 0; ch < buffer.ChannelCount; ch++)
            {
                Assert.That(buffer.GetChannelData(ch).ToArray(), Is.All.Zero);
            }
        }
    }
}
