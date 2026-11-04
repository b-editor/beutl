using Beutl.Extensions.FFmpeg.Encoding;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public sealed class FFmpegEncodingControllerProxyTests
{
    [TestCase("out.wav", false)]
    [TestCase("out.WAV", false)]
    [TestCase("out.mp3", false)]
    [TestCase("out.mp4", true)]
    [TestCase("out.mov", true)]
    [TestCase("out.mkv", true)]
    public void IsVideoEnabled_FollowsTheContainer(string file, bool expected)
    {
        var controller = new FFmpegEncodingControllerProxy(file, new FFmpegEncodingSettings());

        Assert.That(controller.IsVideoEnabled, Is.EqualTo(expected));
    }
}
