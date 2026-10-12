using Beutl.Extensions.FFmpeg;
using Beutl.FFmpegIpc.Transport;

namespace Beutl.FFmpegIpc.Tests;

internal static class DecodingWorkerTestHelpers
{
    /// <summary>
    /// Starts the shared decoding worker and returns its connection, or skips the test when the worker is not
    /// deployed next to the tests or the FFmpeg natives are missing. Under BEUTL_REQUIRE_FFMPEG, CI fails on
    /// either skip reason.
    /// </summary>
    public static IpcConnection RequireDecodingWorker()
    {
        if (!FFmpegWorkerProcess.IsWorkerAvailable(AppContext.BaseDirectory))
        {
            Assert.Ignore("FFmpeg worker binary not present in the test output; skipping.");
        }

        try
        {
            return FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            Assert.Ignore($"FFmpeg natives unavailable ({ex.Message}); skipping.");
            throw; // Assert.Ignore always throws.
        }
    }
}
