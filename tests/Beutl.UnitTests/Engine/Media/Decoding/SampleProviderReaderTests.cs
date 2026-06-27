using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;

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

        public int Read(float[] buffer, int offset, int count)
        {
            int available = Math.Max(0, _totalFrames - _position);
            int frames = Math.Min(Math.Min(count / 2, available), _maxReturnFrames);
            for (int i = 0; i < frames; i++)
            {
                int idx = _position + i;
                buffer[offset + i * 2] = idx;
                buffer[offset + i * 2 + 1] = -idx;
            }
            _position += frames;
            return frames * 2;
        }

        public int Position => _position;
    }

    // Returns an odd number of floats per call to prove the /2 frame mapping truncates a stray float
    // instead of over/under-running the destination.
    private sealed class OddFloatProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int floats = Math.Min(count, 3); // always 3 floats = 1.5 frames
            for (int i = 0; i < floats; i++)
                buffer[offset + i] = i;
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
            Assert.That(((Pcm<Stereo32BitFloat>)result.Value).NumSamples, Is.Zero,
                $"length {length} must yield an empty buffer");
        }
    }

    [Test]
    public void ReadStereo_FullRead_ReturnsRequestedLengthAndData()
    {
        var provider = new FakeSampleProvider(totalFrames: 1000);

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 500);
        var pcm = (Pcm<Stereo32BitFloat>)result.Value;

        Assert.That(pcm.NumSamples, Is.EqualTo(500));
        Span<Stereo32BitFloat> data = pcm.DataSpan;
        Assert.That(data[0].Left, Is.EqualTo(0f));
        Assert.That(data[0].Right, Is.EqualTo(0f));
        Assert.That(data[499].Left, Is.EqualTo(499f));
        Assert.That(data[499].Right, Is.EqualTo(-499f));
    }

    [Test]
    public void ReadStereo_PartialReadNearEof_ReturnsShortBuffer()
    {
        // Only 300 frames available but 500 requested: the result must be a short read of 300.
        var provider = new FakeSampleProvider(totalFrames: 300);

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 500);
        var pcm = (Pcm<Stereo32BitFloat>)result.Value;

        Assert.That(pcm.NumSamples, Is.EqualTo(300), "EOF must surface as a short read, not an error");
        Assert.That(pcm.DataSpan[299].Left, Is.EqualTo(299f));
    }

    [Test]
    public void ReadStereo_ProviderExhausted_ReturnsEmptyBuffer()
    {
        // Provider at EOF returns 0 floats: result is empty, signalling end-of-stream via NumSamples.
        var provider = new FakeSampleProvider(totalFrames: 0);

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 500);
        Assert.That(((Pcm<Stereo32BitFloat>)result.Value).NumSamples, Is.Zero);
    }

    [Test]
    public void ReadStereo_ProviderReturningOddFloatCount_TruncatesToWholeFrames()
    {
        var provider = new OddFloatProvider();

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 4);
        var pcm = (Pcm<Stereo32BitFloat>)result.Value;

        // 3 floats = 1 whole frame (the trailing half-frame float is dropped, never overruns).
        Assert.That(pcm.NumSamples, Is.EqualTo(1));
        Assert.That(pcm.DataSpan[0].Left, Is.EqualTo(0f));
        Assert.That(pcm.DataSpan[0].Right, Is.EqualTo(1f));
    }

    [Test]
    public void ReadStereo_ProviderShortsMidStream_ReturnsWhatWasGot()
    {
        // A provider that short-reads mid-stream (cap 50 per call) for a 200-frame request: a single
        // ReadStereo call returns only the first 50, which the contract allows callers to loop over.
        var provider = new FakeSampleProvider(totalFrames: 1000, maxReturnFrames: 50);

        using var result = SampleProviderReader.ReadStereo(provider, SampleRate, length: 200);
        var pcm = (Pcm<Stereo32BitFloat>)result.Value;

        Assert.That(pcm.NumSamples, Is.EqualTo(50), "a mid-stream short read must be reported as-is");
    }
}
