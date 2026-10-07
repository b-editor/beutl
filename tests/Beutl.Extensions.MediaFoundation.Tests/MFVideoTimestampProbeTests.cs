using Beutl.Embedding.MediaFoundation.Decoding;

using Vortice.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

[TestFixture]
public class MFVideoTimestampProbeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ErrorFlag_StopsReadingWithoutUsingTheSampleTimestamp(bool hasSample)
    {
        int calls = 0;
        long timestamp = MFStreamProbe.ReadFirstVideoTimestamp(() =>
        {
            if (++calls > 1)
                Assert.Fail("MF forbids further calls to a source reader after its Error flag.");
            return (hasSample, SourceReaderFlag.Error, 2_000_000L);
        });

        Assert.That(timestamp, Is.Zero);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void InformationalFlags_ContinueUntilTheFirstSample()
    {
        var results = new Queue<(bool, SourceReaderFlag, long)>(new[]
        {
            (false, SourceReaderFlag.StreamTick, 0L),
            (false, SourceReaderFlag.NewStream, 0L),
            (true, SourceReaderFlag.None, 2_000_000L)
        });

        Assert.That(MFStreamProbe.ReadFirstVideoTimestamp(results.Dequeue), Is.EqualTo(2_000_000));
        Assert.That(results, Is.Empty);
    }

    [Test]
    public void EndOfStreamWithoutSamples_StopsReading()
    {
        int calls = 0;
        Assert.That(MFStreamProbe.ReadFirstVideoTimestamp(() =>
        {
            if (++calls > 1)
                Assert.Fail("The probe must stop at EOF.");
            return (false, SourceReaderFlag.EndOfStream, 0L);
        }), Is.Zero);
        Assert.That(calls, Is.EqualTo(1));
    }
}
