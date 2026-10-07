using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.FFmpegIpc.Protocol.Messages;

namespace Beutl.UnitTests.Extensions.FFmpeg;

// A suspended reader releases its file, so the file can be replaced before it reopens. The proxy keeps the
// VideoInfo/AudioInfo of the first open, so it must only adopt a reopened reader with identical streams.
[TestFixture]
public class FFmpegReaderProxyStreamMatchTests
{
    [Test]
    public void IdenticalStreams_Match()
    {
        Assert.That(FFmpegReaderProxy.HasSameStreams(CreateResponse(), CreateResponse()), Is.True);
    }

    private static readonly (string Name, Action<OpenFileResponse> Change)[] s_changes =
    [
        ("FrameRate", r => r.FrameRateNum = 60),
        ("Width", r => r.VideoWidth = 3840),
        ("Height", r => r.VideoHeight = 2160),
        ("Duration", r => r.DurationNum = 20),
        ("NumFrames", r => r.VideoNumFrames = 600),
        ("VideoCodec", r => r.VideoCodecName = "hevc"),
        ("SampleRate", r => r.AudioSampleRate = 44100),
        ("Channels", r => r.AudioNumChannels = 6),
        ("AudioDuration", r => r.AudioDurationNum = 20),
        ("AudioCodec", r => r.AudioCodecName = "opus"),
        ("NoAudio", r => r.HasAudio = false),
        ("NoVideo", r => r.HasVideo = false),
    ];

    [TestCaseSource(nameof(s_changes))]
    public void ChangedStreamProperty_DoesNotMatch((string Name, Action<OpenFileResponse> Change) change)
    {
        OpenFileResponse reopened = CreateResponse();
        change.Change(reopened);

        Assert.That(FFmpegReaderProxy.HasSameStreams(CreateResponse(), reopened), Is.False, change.Name);
    }

    [Test]
    public void ReaderSpecificFields_AreIgnored()
    {
        OpenFileResponse reopened = CreateResponse();
        reopened.ReaderId = 42;
        reopened.VideoSharedMemoryName = "other-video";
        reopened.AudioSharedMemoryName = "other-audio";

        Assert.That(FFmpegReaderProxy.HasSameStreams(CreateResponse(), reopened), Is.True);
    }

    private static OpenFileResponse CreateResponse() => new()
    {
        ReaderId = 1,
        HasVideo = true,
        HasAudio = true,
        VideoSharedMemoryName = "video",
        AudioSharedMemoryName = "audio",
        VideoCodecName = "h264",
        VideoNumFrames = 300,
        VideoWidth = 1920,
        VideoHeight = 1080,
        FrameRateNum = 30,
        FrameRateDen = 1,
        DurationNum = 10,
        DurationDen = 1,
        AudioCodecName = "aac",
        AudioSampleRate = 48000,
        AudioNumChannels = 2,
        AudioDurationNum = 10,
        AudioDurationDen = 1,
    };
}
