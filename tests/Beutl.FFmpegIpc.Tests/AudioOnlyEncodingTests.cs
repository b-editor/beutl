using Beutl.Audio;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Media;

namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public class AudioOnlyEncodingTests
{
    [Test]
    public async Task Encode_WritesTheAudioWithoutAskingForAFrame()
    {
        if (!FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
            Assert.Ignore("FFmpeg worker binary not present in the test output; skipping.");

        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N") + ".wav");
        var controller = new FFmpegEncodingControllerProxy(path, new FFmpegEncodingSettings());
        controller.AudioSettings.Format.Value = FFmpegAudioEncoderSettings.AudioFormat.Default;
        using var frames = new NoFrames();
        using var samples = new Silence();
        try
        {
            await controller.Encode(frames, samples, CancellationToken.None);
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            Assert.Ignore($"FFmpeg natives unavailable ({ex.Message}); skipping.");
        }

        Assert.That(new FileInfo(path).Length, Is.GreaterThan(0));
        File.Delete(path);
    }

    // What the export view model hands an encoder whose output has no video.
    private sealed class NoFrames : IFrameProvider
    {
        public long FrameCount => 0;
        public Rational FrameRate => new(0, 1);
        public ValueTask<Bitmap?> ReadNextFrame() => throw new AssertionException("An audio-only export must not ask for a frame.");
        public void Dispose() { }
    }

    private sealed class Silence : ISampleProvider
    {
        private long _next;

        public AudioChannelLayout ChannelLayout => AudioChannelLayout.Stereo;
        public long SampleCount => 44100;
        public long SampleRate => 44100;

        public ValueTask<AudioBuffer?> ReadNextSamples(int length)
        {
            int count = (int)Math.Min(length, SampleCount - _next);
            _next += count;
            return ValueTask.FromResult(count > 0 ? new AudioBuffer((int)SampleRate, 2, count) : null);
        }

        public void Dispose() { }
    }
}
