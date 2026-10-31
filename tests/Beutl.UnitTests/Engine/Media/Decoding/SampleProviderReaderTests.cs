using Beutl.Media.Decoding;

using NAudio.Wave;

namespace Beutl.UnitTests.Engine.Media.Decoding;

[TestFixture]
public class SampleProviderReaderTests
{
    private const int SampleRate = 44100;

    // A minimal stereo ISampleProvider whose frame N carries Left = N, Right = -N. The total frame
    // count and a per-call return cap let us exercise full, short, empty and odd-float reads.
    private sealed class FakeSampleProvider : ISampleProvider
    {
        private readonly int _totalFrames;
        private readonly int _maxReturnFrames;
        private int _position;

        public FakeSampleProvider(int totalFrames, int maxReturnFrames = int.MaxValue)
        {
            _totalFrames = totalFrames;
            _maxReturnFrames = maxReturnFrames;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            int available = Math.Max(0, _totalFrames - _position);
            int frames = Math.Min(Math.Min(buffer.Length / 2, available), _maxReturnFrames);
            for (int i = 0; i < frames; i++)
            {
                int idx = _position + i;
                buffer[i * 2] = idx;
                buffer[i * 2 + 1] = -idx;
            }
            _position += frames;
            return frames * 2;
        }
    }

    // Returns an odd number of floats per call to prove the /2 frame mapping truncates a stray float
    // instead of over/under-running the destination.
    private sealed class OddFloatProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

        public int Read(Span<float> buffer)
        {
            int floats = Math.Min(buffer.Length, 3); // always 3 floats = 1.5 frames
            for (int i = 0; i < floats; i++)
                buffer[i] = i;
            return floats;
        }
    }

    [Test]
    public void ReadStereo_ZeroOrNegativeLength_ReturnsEmptyBuffer()
    {
        var provider = new FakeSampleProvider(totalFrames: 100);

        foreach (int length in new[] { 0, -1, -50 })
        {
            using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length);
            Assert.That(result.SampleCount, Is.Zero,
                $"length {length} must yield an empty buffer");
        }
    }

    [Test]
    public void ReadStereo_FullRead_ReturnsRequestedLengthAndData()
    {
        var provider = new FakeSampleProvider(totalFrames: 1000);

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 500);

        Assert.That(result.SampleCount, Is.EqualTo(500));
        Span<float> left = result.GetChannelData(0);
        Span<float> right = result.GetChannelData(1);
        Assert.That(left[0], Is.EqualTo(0f));
        Assert.That(right[0], Is.EqualTo(0f));
        Assert.That(left[499], Is.EqualTo(499f));
        Assert.That(right[499], Is.EqualTo(-499f));
    }

    [Test]
    public void ReadStereo_PartialReadNearEof_ReturnsShortBuffer()
    {
        var provider = new FakeSampleProvider(totalFrames: 300);

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 500);

        Assert.That(result.SampleCount, Is.EqualTo(300), "EOF must surface as a short read, not an error");
        Assert.That(result.GetChannelData(0)[299], Is.EqualTo(299f));
    }

    [Test]
    public void ReadStereo_ProviderExhausted_ReturnsEmptyBuffer()
    {
        // Provider at EOF returns 0 floats: result is empty, signalling end-of-stream via NumSamples.
        var provider = new FakeSampleProvider(totalFrames: 0);

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 500);
        Assert.That(result.SampleCount, Is.Zero);
    }

    [Test]
    public void ReadStereo_ProviderReturningOddFloatCount_TruncatesToWholeFrames()
    {
        var provider = new OddFloatProvider();

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 4);

        // 3 floats = 1 whole frame (the trailing half-frame float is dropped, never overruns).
        Assert.That(result.SampleCount, Is.EqualTo(1));
        Assert.That(result.GetChannelData(0)[0], Is.EqualTo(0f));
        Assert.That(result.GetChannelData(1)[0], Is.EqualTo(1f));
    }

    [Test]
    public void ReadStereo_ProviderShortsMidStream_ReturnsWhatWasGot()
    {
        // A provider that short-reads mid-stream (cap 50 per call) for a 200-frame request: a single
        // ReadStereo call returns only the first 50, which the contract allows callers to loop over.
        var provider = new FakeSampleProvider(totalFrames: 1000, maxReturnFrames: 50);

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 200);

        Assert.That(result.SampleCount, Is.EqualTo(50), "a mid-stream short read must be reported as-is");
    }

    [TestCase(1, 12, 10, 10)]
    [TestCase(6, 60, 10, 10)]
    [TestCase(6, 14, 10, 2)]
    [TestCase(9, 20, 10, 2)]
    [TestCase(6, 0, 10, 0)]
    [TestCase(6, 60, 0, 0)]
    [TestCase(6, 60, -1, 0)]
    public void Read_NativeChannels_PreservesOrderAndReturnsOnlyWholeFrames(int channels, int elements, int length, int expectedFrames)
    {
        var provider = new NativeProvider(channels, elements);
        using var result = SampleProviderReader.Read(provider, SampleRate, channels, length);
        Assert.That(result.ChannelCount, Is.EqualTo(channels));
        Assert.That(result.SampleRate, Is.EqualTo(SampleRate));
        Assert.That(result.SampleCount, Is.EqualTo(expectedFrames));
        Assert.That(provider.ReadCount, Is.EqualTo(length > 0 ? 1 : 0));
        for (int ch = 0; ch < channels; ch++)
            Assert.That(result.GetChannelData(ch).ToArray(), Is.EqualTo(Enumerable.Range(0, expectedFrames).Select(i => (float)(i * channels + ch))));
    }

    private sealed class NativeProvider(int channels, int elements) : ISampleProvider
    {
        public int ReadCount { get; private set; }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, channels);

        public int Read(Span<float> buffer)
        {
            ReadCount++;
            int count = Math.Min(elements, buffer.Length);
            for (int i = 0; i < count; i++) buffer[i] = i;
            return count;
        }
    }

}
