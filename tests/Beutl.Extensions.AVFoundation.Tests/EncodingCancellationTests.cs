using Beutl.Extensions.AVFoundation.Encoding;
using Beutl.Media;

namespace Beutl.Extensions.AVFoundation.Tests;

// A cancelled Encode must throw OperationCanceledException (both callers already catch it). This
// AVF test pins the shared contract.
[TestFixture]
[Platform("MacOSX")]
public class EncodingCancellationTests
{
    private string _workDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-avf-cancel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Test]
    public void EncodeSurfacesCancellationAsOperationCanceledException()
    {
        string outputPath = Path.Combine(_workDir, "cancelled.mp4");
        var controller = new AVFEncodingController(outputPath);

        const int width = 64;
        const int height = 64;
        const int sampleRate = 44100;
        const int frameCount = 30;
        const int frameRateNum = 30;
        const int frameRateDen = 1;

        controller.VideoSettings.DestinationSize = new PixelSize(width, height);
        controller.VideoSettings.SourceSize = new PixelSize(width, height);
        controller.VideoSettings.FrameRate = new Rational(frameRateNum, frameRateDen);

        controller.AudioSettings.SampleRate = sampleRate;
        controller.AudioSettings.Channels = 2;

        using var cts = new CancellationTokenSource();
        // Cancel mid-clip (after one frame) so the controller exits its loop on cancellation.
        var frameProvider = new CancelAfterFirstFrameProvider(
            cts, frameCount, new Rational(frameRateNum, frameRateDen), width, height);
        var sampleProvider = new SineSampleProvider(sampleRate, sampleRate);

        Assert.That(
            async () => await controller.Encode(frameProvider, sampleProvider, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
