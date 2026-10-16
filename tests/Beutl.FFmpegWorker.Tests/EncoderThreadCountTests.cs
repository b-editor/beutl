using Beutl.FFmpegWorker.Encoding;

namespace Beutl.FFmpegWorker.Tests;

// The encoding settings' ThreadCount reaches the codec contexts, read the way the decoder reads its own.
public class EncoderThreadCountTests
{
    [Test]
    public void The_default_keeps_the_automatic_count_of_up_to_sixteen()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FFmpegEncodingController.ResolveThreadCount(-1), Is.EqualTo(Math.Min(Environment.ProcessorCount, 16)));
            Assert.That(FFmpegEncodingController.ResolveThreadCount(-5), Is.EqualTo(Math.Min(Environment.ProcessorCount, 16)));
        });
    }

    [Test]
    public void Zero_lets_ffmpeg_choose()
    {
        Assert.That(FFmpegEncodingController.ResolveThreadCount(0), Is.Zero);
    }

    [Test]
    public void A_positive_count_is_used_up_to_the_processor_count()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FFmpegEncodingController.ResolveThreadCount(1), Is.EqualTo(1));
            Assert.That(FFmpegEncodingController.ResolveThreadCount(int.MaxValue), Is.EqualTo(Environment.ProcessorCount));
        });
    }
}
