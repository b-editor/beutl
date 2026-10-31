using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Composition;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using NUnit.Framework;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SourceNodeTests
{
    [TestCase(-2000L, false)]
    [TestCase(2000L, true)]
    public void Process_ReportsUnavailableOffsetsWithoutChangingPlaybackFallback(long offset, bool failed)
    {
        TestMediaHelper.RegisterTestDecoder();
        var source = SoundSource.Open(TestMediaHelper.CreateTestAudioFile(192000, durationSeconds: 28800));
        using var resource = source.ToResource(CompositionContext.Default);
        using var node = new SourceNode { Source = (resource, 0) };
        using var reads = new AudioReadScope();
        var range = new TimeRange(MediaTime.FromSamples(int.MaxValue + offset, 192000), MediaTime.FromSeconds(0.01));
        using var buffer = node.Process(new AudioProcessContext(range, 192000, new AnimationSampler(), null));
        Assert.That(reads.HasFailures, Is.EqualTo(failed));
        Assert.That(buffer.GetChannelData(0).ToArray().All(value => value == 0), Is.EqualTo(failed));
    }

    // Regression: a source whose media failed to open has SampleRate == 0. SourceNode must return a
    // silent buffer for it, not throw from TimeToSampleIndex on the zero rate.
    [Test]
    public void Process_UnloadedSourceWithZeroSampleRate_ReturnsSilenceInsteadOfThrowing()
    {
        var resource = new SoundSource.Resource();
        Assume.That(resource.SampleRate, Is.Zero, "precondition: an unloaded resource has SampleRate 0");

        var node = new SourceNode { Source = (resource, 0) };
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        var context = new AudioProcessContext(range, 44100, new AnimationSampler(), null);

        AudioBuffer? buffer = null;
        Assert.DoesNotThrow(() => buffer = node.Process(context));

        using (buffer)
        {
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer!.SampleCount, Is.EqualTo(context.GetSampleCount()));

            float[] left = buffer.GetChannelData(0).ToArray();
            float[] right = buffer.GetChannelData(1).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(left, Is.All.EqualTo(0f), "left channel must be silent");
                Assert.That(right, Is.All.EqualTo(0f), "right channel must be silent");
            });
        }
    }

    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(2, true)]
    public void Process_MonoAndStereoCopyEveryBitAndKeepTheShortReadTailSilent(int channels, bool mismatchedLayout)
    {
        int[] bits = [unchecked((int)0x80000000), 0x7FC12345, 0x7F800000, unchecked((int)0xFF800000)];
        using var samples = AudioTestBuffers.CreateBuffer(channels, bits.Length, (ch, i) => BitConverter.Int32BitsToSingle(bits[(i + ch) % bits.Length]));
        var layout = mismatchedLayout ? AudioChannelLayout.GetDefault(3 - channels) : AudioChannelLayout.GetDefault(channels);
        using var reader = new AudioTestReader(samples, layout);
        WithReader(reader, resource =>
        {
            Assert.That(resource.ChannelLayout, Is.EqualTo(layout));
            using var node = new SourceNode { Source = (resource, 0) };
            using var reads = new AudioReadScope();
            using var output = node.Process(AudioTestContexts.Exact(MediaTime.Zero, 8, samples.SampleRate));
            Assert.That(output.ChannelCount, Is.EqualTo(2));
            Assert.That(reads.HasFailures, Is.False);
            for (int ch = 0; ch < 2; ch++)
            {
                Assert.That(output.GetChannelData(ch)[..4].ToArray().Select(BitConverter.SingleToInt32Bits),
                    Is.EqualTo(samples.GetChannelData(Math.Min(ch, channels - 1)).ToArray().Select(BitConverter.SingleToInt32Bits)));
                Assert.That(output.GetChannelData(ch)[4..].ToArray(), Is.All.Zero);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Process_SixChannelsUseTheItuFoldEvenWhenTheDeclaredCountDiffers(bool mismatchedLayout)
    {
        using var samples = AudioTestBuffers.CreateBuffer(6, 4, (ch, _) => ch == 3 ? 1000 : ch + 1);
        using var reader = new AudioTestReader(samples, mismatchedLayout ? AudioChannelLayout.Stereo : AudioChannelLayout.Surround51);
        WithReader(reader, resource =>
        {
            using var node = new SourceNode { Source = (resource, 0) };
            using var output = node.Process(AudioTestContexts.Exact(MediaTime.Zero, 4, samples.SampleRate));
            Assert.That(output.GetChannelData(0).ToArray(), Is.All.EqualTo(1 + 0.70710677f * 3 + 0.70710677f * 5));
            Assert.That(output.GetChannelData(1).ToArray(), Is.All.EqualTo(2 + 0.70710677f * 3 + 0.70710677f * 6));
        });
    }

    [Test]
    public void Process_UsesDeclaredPositionsInsteadOfGuessingFromTheCount()
    {
        var layout = new AudioChannelLayout(4, AudioSpeakers.FrontLeft | AudioSpeakers.FrontRight | AudioSpeakers.FrontCenter | AudioSpeakers.BackCenter);
        using var samples = AudioTestBuffers.CreateBuffer(4, 1, (ch, _) => ch + 1);
        using var reader = new AudioTestReader(samples, layout);
        WithReader(reader, resource =>
        {
            using var node = new SourceNode { Source = (resource, 0) };
            using var output = node.Process(AudioTestContexts.Exact(MediaTime.Zero, 1, samples.SampleRate));
            Assert.That(output.GetChannelData(0)[0], Is.EqualTo(1 + 0.70710677f * 3 + 0.5f * 4));
            Assert.That(output.GetChannelData(1)[0], Is.EqualTo(2 + 0.70710677f * 3 + 0.5f * 4));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Process_EmptyOrFailedReadsKeepStereoSilenceAndFailureReporting(bool fail)
    {
        using var samples = new AudioBuffer(48000, 6, 0);
        using var reader = new AudioTestReader(samples, AudioChannelLayout.Surround51) { FailReads = fail };
        WithReader(reader, resource =>
        {
            using var node = new SourceNode { Source = (resource, 0) };
            using var reads = new AudioReadScope();
            using var output = node.Process(AudioTestContexts.Exact(MediaTime.Zero, 4, samples.SampleRate));
            Assert.That(output.ChannelCount, Is.EqualTo(2));
            Assert.That(output.GetChannelData(0).ToArray(), Is.All.Zero);
            Assert.That(output.GetChannelData(1).ToArray(), Is.All.Zero);
            Assert.That(reads.HasFailures, Is.EqualTo(fail));
        });
    }

    [Test]
    public void Process_NineDiscreteChannelsCopyOnlyTheFirstPair()
    {
        using var samples = AudioTestBuffers.CreateBuffer(9, 2, (ch, _) => ch < 2 ? ch + 1 : float.NaN);
        using var reader = new AudioTestReader(samples, AudioChannelLayout.Discrete(9));
        WithReader(reader, resource =>
        {
            using var node = new SourceNode { Source = (resource, 0) };
            using var output = node.Process(AudioTestContexts.Exact(MediaTime.Zero, 2, samples.SampleRate));
            Assert.That(output.GetChannelData(0).ToArray(), Is.All.EqualTo(1));
            Assert.That(output.GetChannelData(1).ToArray(), Is.All.EqualTo(2));
        });
    }

    private static void WithReader(MediaReader reader, Action<SoundSource.Resource> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.nativeaudio");
        var decoder = new BufferDecoder(path, reader);
        File.WriteAllBytes(path, []);
        DecoderRegistry.Register(decoder);
        try
        {
            var source = SoundSource.Open(path);
            using var resource = source.ToResource(new CompositionContext(MediaTime.Zero) { DisableResourceShare = true });
            Assert.That(resource.MediaReader, Is.SameAs(reader));
            body(resource);
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            File.Delete(path);
        }
    }

    private sealed class BufferDecoder(string path, MediaReader reader) : IDecoderInfo
    {
        public string Name => "Native audio test";
        public MediaReader? Open(string file, MediaOptions options) => file == path ? reader : null;
        public IEnumerable<string> VideoExtensions() => [];
        public IEnumerable<string> AudioExtensions() => [".nativeaudio"];
    }

}
