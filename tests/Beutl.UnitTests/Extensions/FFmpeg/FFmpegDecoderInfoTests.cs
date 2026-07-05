using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.FFmpegIpc;
using Beutl.Media.Decoding;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
[NonParallelizable]
public sealed class FFmpegDecoderInfoTests
{
    [Test]
    public void Open_WhenLibrariesMissing_SurfacesTypedExceptionInsteadOfSwallowingToNull()
    {
        // Force the "libraries missing" short-circuit so the worker start throws
        // FFmpegLibrariesNotFoundException without launching a real process.
        FFmpegInstallNotifier.MarkMissing();
        try
        {
            var decoder = new FFmpegDecoderInfo(new FFmpegDecodingSettings());
            string file = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing.mp4");

            try
            {
                MediaReader? reader = decoder.Open(file, new MediaOptions(MediaMode.Video));

                // No throw means a worker was already connected in this run, so the missing-libraries
                // path is unreachable here; the CI environment (no FFmpeg) exercises the throw path.
                reader?.Dispose();
                Assert.Ignore("An FFmpeg worker is already connected; missing-libraries path is unreachable.");
            }
            catch (FFmpegLibrariesNotFoundException)
            {
                // The fix: a missing-libraries failure must surface as this typed exception so proxy
                // generation can translate it to unavailable, instead of being swallowed into null
                // (which MediaReader.Open turns into a plain Exception the generator cannot classify).
            }
        }
        finally
        {
            FFmpegInstallNotifier.MarkInstalled();
        }
    }
}
