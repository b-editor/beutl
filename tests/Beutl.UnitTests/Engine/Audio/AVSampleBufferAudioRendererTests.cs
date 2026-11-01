using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Beutl.Audio;
using Beutl.Audio.Platforms.AVFoundation;
using static Beutl.Audio.Platforms.AVFoundation.AVFoundationNative;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
[Platform("MacOSX")]
[SupportedOSPlatform("macos")]
public class AVSampleBufferAudioRendererTests
{
    [TestCase(6, 44100, (121u << 16) | 6)]
    [TestCase(6, 48000, (121u << 16) | 6)]
    [TestCase(8, 48000, (189u << 16) | 8)]
    [TestCase(8, 96000, (189u << 16) | 8)]
    public unsafe void FormatDescription_RoundTripsProjectLayoutAndFloatPcm(int channels, int sampleRate, uint tag)
    {
        using var pool = new AutoreleasePool();
        nint format = AVSampleBufferAudioRenderer.CreateFormatDescription(AudioChannelLayout.GetDefault(channels), sampleRate);
        try
        {
            AudioStreamBasicDescription* asbd = CMAudioFormatDescriptionGetStreamBasicDescription(format);
            ChannelLayout* layout = CMAudioFormatDescriptionGetChannelLayout(format, out nuint layoutSize);
            Assert.That((nint)asbd, Is.Not.EqualTo(nint.Zero));
            Assert.That((nint)layout, Is.Not.EqualTo(nint.Zero));
            Assert.That(layoutSize, Is.GreaterThanOrEqualTo((nuint)12));
            Assert.Multiple(() =>
            {
                Assert.That(layout->Tag, Is.EqualTo(tag));
                Assert.That(layout->Bitmap, Is.Zero);
                Assert.That(layout->NumberChannelDescriptions, Is.Zero);
                Assert.That(asbd->SampleRate, Is.EqualTo(sampleRate));
                Assert.That(asbd->ChannelsPerFrame, Is.EqualTo(channels));
                Assert.That(asbd->FormatId, Is.EqualTo(0x6c70636du));
                Assert.That(asbd->FormatFlags, Is.EqualTo(9u)); // Float, packed, interleaved
                Assert.That(asbd->BitsPerChannel, Is.EqualTo(32));
                Assert.That(asbd->FramesPerPacket, Is.EqualTo(1));
                Assert.That(asbd->BytesPerFrame, Is.EqualTo(channels * sizeof(float)));
                Assert.That(asbd->BytesPerPacket, Is.EqualTo(asbd->BytesPerFrame));
            });
        }
        finally
        {
            CFRelease(format);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public unsafe void FormatDescription_DoesNotRelabelOtherLayoutsAsSurround(bool discrete)
    {
        AudioChannelLayout project = discrete ? AudioChannelLayout.Discrete(6) : AudioChannelLayout.GetDefault(4);
        using var pool = new AutoreleasePool();
        nint format = AVSampleBufferAudioRenderer.CreateFormatDescription(project, 48000);
        try
        {
            ChannelLayout* layout = CMAudioFormatDescriptionGetChannelLayout(format, out _);
            Assert.That((nint)layout, Is.Not.EqualTo(nint.Zero));
            Assert.That(layout->Tag, Is.EqualTo(discrete ? (147u << 16) | 6 : 1u << 16));
            Assert.That(layout->Bitmap, Is.EqualTo((uint)project.Speakers));
        }
        finally
        {
            CFRelease(format);
        }
    }

    [TestCase(6, 44100)]
    [TestCase(8, 48000)]
    public unsafe void SampleBuffer_OwnsInterleavedSamplesAndSampleIndexTimestamp(int channels, int sampleRate)
    {
        const int samples = 5;
        const long firstSample = (1L << 34) + 123; // Exercise the full int64 CMTime value.
        using var pool = new AutoreleasePool();
        using var audio = new AudioBuffer(sampleRate, channels, samples);
        for (int channel = 0; channel < channels; channel++)
        {
            for (int frame = 0; frame < samples; frame++)
                audio.GetChannelData(channel)[frame] = channel + frame / 16f;
        }

        nint format = AVSampleBufferAudioRenderer.CreateFormatDescription(AudioChannelLayout.GetDefault(channels), sampleRate);
        nint buffer;
        try
        {
            buffer = AVSampleBufferAudioRenderer.CreateSampleBuffer(format, audio, firstSample);
        }
        finally
        {
            CFRelease(format);
        }
        // Neither the source buffer nor the caller's format reference owns the queued data.
        audio.Clear();
        try
        {
            Assert.That(CMSampleBufferGetNumSamples(buffer), Is.EqualTo((nint)samples));
            CMTime pts = CMSampleBufferGetPresentationTimeStamp(buffer);
            Assert.Multiple(() =>
            {
                Assert.That(pts.Value, Is.EqualTo(firstSample));
                Assert.That(pts.Timescale, Is.EqualTo(sampleRate));
                Assert.That(pts.Flags, Is.EqualTo(1u));
                Assert.That(pts.Epoch, Is.Zero);
            });
            nint block = CMSampleBufferGetDataBuffer(buffer); // Borrowed from the sample buffer.
            Assert.That(CMBlockBufferGetDataPointer(block, 0, out nuint length, out nuint total, out nint data), Is.Zero);
            Assert.That(total, Is.EqualTo((nuint)(samples * channels * sizeof(float))));
            Assert.That(length, Is.EqualTo(total));
            var interleaved = new ReadOnlySpan<float>((void*)data, samples * channels);
            for (int frame = 0; frame < samples; frame++)
            {
                for (int channel = 0; channel < channels; channel++)
                    Assert.That(interleaved[frame * channels + channel], Is.EqualTo(channel + frame / 16f));
            }
        }
        finally
        {
            CFRelease(buffer);
        }
    }

    [Test]
    public void CMTime_MatchesTheNativeAbi()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<CMTime>(), Is.EqualTo(24));
            Assert.That(Marshal.OffsetOf<CMTime>(nameof(CMTime.Value)), Is.EqualTo((nint)0));
            Assert.That(Marshal.OffsetOf<CMTime>(nameof(CMTime.Timescale)), Is.EqualTo((nint)8));
            Assert.That(Marshal.OffsetOf<CMTime>(nameof(CMTime.Flags)), Is.EqualTo((nint)12));
            Assert.That(Marshal.OffsetOf<CMTime>(nameof(CMTime.Epoch)), Is.EqualTo((nint)16));
        });
    }

    [TestCase(6)]
    [TestCase(8)]
    public void Renderer_EnqueuesAtRateZeroAndDisposesWithoutAnAudioDevice(int channels)
    {
        AVSampleBufferAudioRenderer renderer;
        try
        {
            renderer = new AVSampleBufferAudioRenderer(AudioChannelLayout.GetDefault(channels), 48000);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or InvalidOperationException or AVSampleBufferAudioRendererException)
        {
            Assert.Ignore($"A headless audio renderer is unavailable: {ex.Message}");
            return;
        }

        using (renderer)
        {
            using var audio = new AudioBuffer(48000, channels, 480);
            try
            {
                for (int chunk = 0; chunk < 3; chunk++)
                {
                    if (!renderer.TryEnqueue(audio, chunk * audio.SampleCount))
                        Assert.Ignore("The headless renderer cannot accept buffers at rate 0.");
                }
                Assert.That(renderer.PositionSeconds, Is.Zero);
            }
            catch (AVSampleBufferAudioRendererException ex)
            {
                Assert.Ignore($"The headless renderer cannot queue audio: {ex.Message}");
            }

            renderer.Stop();
            Assert.That(renderer.TryEnqueue(audio, 1440), Is.False, "a stopped session cannot enqueue late composition results");
            renderer.Stop();
        }
        Assert.DoesNotThrow(renderer.Dispose);
    }
}
