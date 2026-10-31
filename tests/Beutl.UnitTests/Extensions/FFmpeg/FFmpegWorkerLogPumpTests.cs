using Beutl.Extensions.FFmpeg;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Extensions.FFmpeg;

public class FFmpegWorkerLogPumpTests
{
    [TestCase("[error] cannot open input", LogLevel.Error)]
    [TestCase("[h264 @ 0x123] [warning] invalid packet", LogLevel.Warning)]
    [TestCase("[parent @ 0x123] [aac @ 0x456] [fatal] decode failed", LogLevel.Critical)]
    [TestCase("[panic] allocation failed", LogLevel.Critical)]
    [TestCase("[info] stream info", LogLevel.Information)]
    [TestCase("[debug] probing", LogLevel.Debug)]
    [TestCase("[verbose] format details", LogLevel.Trace)]
    [TestCase("[trace] index entry", LogLevel.Trace)]
    [TestCase("unclassified output", LogLevel.Warning)]
    [TestCase("message containing [error] is not a level tag", LogLevel.Warning)]
    [TestCase("[unfinished", LogLevel.Warning)]
    public void NativeStderrPreservesSeverityAndMessage(string message, LogLevel expected)
    {
        Assert.That(FFmpegWorkerLogPump.ParseLevel("stderr", message), Is.EqualTo((expected, message)));
    }

    [Test]
    public void ManagedMessagesKeepTheirEscapingAndUntaggedStdoutUsesInformation()
    {
        Assert.That(FFmpegWorkerLogPump.ParseLevel("stdout", "[ffmpeg:Error] first\\nsecond\\\\path"),
            Is.EqualTo((LogLevel.Error, "first\nsecond\\path")));
        Assert.That(FFmpegWorkerLogPump.ParseLevel("stdout", "[error] ordinary stdout"),
            Is.EqualTo((LogLevel.Information, "[error] ordinary stdout")));
    }
}
