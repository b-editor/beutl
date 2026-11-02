using Beutl.Audio;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class AudioSampleRingBufferTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    [TestCase(8)]
    public void SnapshotChannelsAreFoldedWithTheSharedMatrix(int channels)
    {
        using var input = new AudioBuffer(48000, channels, 32);
        for (int ch = 0; ch < channels; ch++) input.GetChannelData(ch).Fill((ch + 1) / 32f);
        var samples = new float[32 * channels];
        AudioConversion.Interleave(input, samples);
        using var expected = AudioChannelMatrix.Create(AudioChannelLayout.GetDefault(channels), AudioChannelLayout.Stereo).Apply(input);
        var ring = new AudioSampleRingBuffer();
        ring.WriteInterleaved(samples, channels, 48000, MediaTime.Zero);
        var left = new float[32];
        var right = new float[32];
        ring.ReadAroundTime(MediaTime.FromSamples(32, 48000), left, right, 32);
        Assert.That(left, Is.EqualTo(expected.GetChannelData(0).ToArray()));
        Assert.That(right, Is.EqualTo(expected.GetChannelData(1).ToArray()));
    }

    [TestCase(6)]
    [TestCase(8)]
    public void SurroundChannelsAreAlsoKeptUnfolded(int channels)
    {
        var ring = new AudioSampleRingBuffer();
        ring.WriteInterleaved(Interleave(channels, 32, ch => (ch + 1) / 16f), channels, 48000, MediaTime.Zero);
        float[][] dest = Enumerable.Range(0, channels).Select(_ => new float[32]).ToArray();

        int got = ring.ReadChannelsAroundTime(MediaTime.FromSamples(32, 48000), dest, 32, out int leadingZeros);

        Assert.Multiple(() =>
        {
            Assert.That(ring.ChannelCount, Is.EqualTo(channels));
            Assert.That(got, Is.EqualTo(32));
            Assert.That(leadingZeros, Is.Zero);
            for (int ch = 0; ch < channels; ch++)
                Assert.That(dest[ch], Is.All.EqualTo((ch + 1) / 16f), $"channel {ch}");
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void StereoAndMonoReadBackTheirOwnChannels(int channels)
    {
        var ring = new AudioSampleRingBuffer();
        ring.WriteInterleaved(Interleave(channels, 32, ch => (ch + 1) / 4f), channels, 48000, MediaTime.Zero);
        float[][] dest = Enumerable.Range(0, channels).Select(_ => new float[32]).ToArray();

        int got = ring.ReadChannelsAroundTime(MediaTime.FromSamples(32, 48000), dest, 32, out _);

        Assert.That(got, Is.EqualTo(32));
        for (int ch = 0; ch < channels; ch++)
            Assert.That(dest[ch], Is.All.EqualTo((ch + 1) / 4f), $"channel {ch}");
    }

    [Test]
    public void ChannelMetersAreMeasuredPerChannel()
    {
        var ring = new AudioSampleRingBuffer();
        // Only the centre channel of 5.1 carries signal; folding would spread it over L and R.
        ring.WriteInterleaved(Interleave(6, 256, ch => ch == 2 ? 0.5f : 0f), 6, 48000, MediaTime.Zero);
        float[][] scratch = Enumerable.Range(0, 6).Select(_ => new float[256]).ToArray();
        var rms = new float[6];
        var peak = new float[6];

        int count = ring.ComputeChannelMeters(256, MediaTime.FromSamples(256, 48000), scratch, rms, peak);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(6));
            Assert.That(rms, Is.EqualTo(new[] { 0f, 0f, 0.5f, 0f, 0f, 0f }).Within(1e-6f));
            Assert.That(peak, Is.EqualTo(new[] { 0f, 0f, 0.5f, 0f, 0f, 0f }).Within(1e-6f));
        });
    }

    [Test]
    public void ChannelReadRejectsMismatchedShape()
    {
        var ring = new AudioSampleRingBuffer();
        ring.WriteInterleaved(Interleave(6, 32, _ => 1f), 6, 48000, MediaTime.Zero);
        float[][] dest = [new float[32], new float[32]];

        Assert.That(ring.ReadChannelsAroundTime(MediaTime.FromSamples(32, 48000), dest, 32, out _), Is.Zero);
    }

    // Stepping a paused playhead writes a window that starts before the last one ended. The reset
    // keeps the ring arrays, so the first write's samples are still there past the new write index.
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    [TestCase(8)]
    public void BackwardOverlapKeepsTheRingArraysButHidesTheirOldSamples(int channels)
    {
        const int sampleRate = 48000;
        const int window = 64;
        MediaTime start = MediaTime.FromSamples(32, sampleRate);
        float[] overlap = Interleave(channels, 16, _ => 0.25f);
        var ring = new AudioSampleRingBuffer();
        ring.WriteInterleaved(Interleave(channels, window, _ => 1f), channels, sampleRate, MediaTime.Zero);

        long before = GC.GetAllocatedBytesForCurrentThread();
        ring.WriteInterleaved(overlap, channels, sampleRate, start);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.LessThan(sampleRate * 5 * sizeof(float)), "The reset must keep the ring arrays.");

        // A ring that never held the old samples gives the expected stereo fold.
        var fresh = new AudioSampleRingBuffer();
        fresh.WriteInterleaved(overlap, channels, sampleRate, start);
        float[][] dest = Enumerable.Range(0, channels).Select(_ => new float[window]).ToArray();
        var left = new float[window];
        var right = new float[window];
        var expectedLeft = new float[window];
        var expectedRight = new float[window];

        // Inside the new write, at and past its end, and before its start, where the first write reached.
        foreach ((int endSample, int real) in new[] { (40, 8), (48, 16), (96, 16), (16, 0) })
        {
            MediaTime playhead = MediaTime.FromSamples(endSample, sampleRate);
            foreach (float[] channel in dest) Array.Fill(channel, -1f);
            Array.Fill(left, -1f);
            Array.Fill(right, -1f);
            float[] expected = [.. Enumerable.Repeat(0f, window - real), .. Enumerable.Repeat(0.25f, real)];

            int got = ring.ReadChannelsAroundTime(playhead, dest, window, out int leadingZeros);

            Assert.That(got, Is.EqualTo(real), $"playhead at {endSample}");
            Assert.That(leadingZeros, Is.EqualTo(real == 0 ? 0 : window - real), $"playhead at {endSample}");
            foreach (float[] channel in dest) Assert.That(channel, Is.EqualTo(expected), $"playhead at {endSample}");

            got = ring.ReadAroundTime(playhead, left, right, window);
            fresh.ReadAroundTime(playhead, expectedLeft, expectedRight, window);

            Assert.That(got, Is.EqualTo(real), $"playhead at {endSample}");
            Assert.That(left, Is.EqualTo(expectedLeft), $"playhead at {endSample}");
            Assert.That(right, Is.EqualTo(expectedRight), $"playhead at {endSample}");
        }
    }

    private static float[] Interleave(int channels, int frames, Func<int, float> value)
    {
        var samples = new float[channels * frames];
        for (int f = 0; f < frames; f++)
        {
            for (int ch = 0; ch < channels; ch++) samples[f * channels + ch] = value(ch);
        }

        return samples;
    }
}
