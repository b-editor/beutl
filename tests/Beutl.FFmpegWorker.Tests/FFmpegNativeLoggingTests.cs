using System.Runtime.InteropServices;
using Beutl.FFmpegWorker;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture, NonParallelizable]
public class FFmpegNativeLoggingTests
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void NativeLog(void* context, int level, byte* format);

    [TestCase(ffmpeg.AV_LOG_WARNING)]
    [TestCase(ffmpeg.AV_LOG_VERBOSE)]
    public unsafe void DisabledNativeTraceDoesNotAllocateManagedMessages(int level)
    {
        FFmpegTestHelpers.RequireFFmpeg();
        FFmpegLoaderWorker.SetupLogging();
        int previousLevel = ffmpeg.av_log_get_level();
        try
        {
            ffmpeg.av_log_set_level(level);
            var log = DynamicallyLoadedBindings.FunctionResolver.GetFunctionDelegate<NativeLog>("avutil", "av_log", true);
            fixed (byte* format = "index entry that must remain in native code\n\0"u8)
            {
                for (int i = 0; i < 100; i++) log(null, ffmpeg.AV_LOG_TRACE, format);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20_000; i++) log(null, ffmpeg.AV_LOG_TRACE, format);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.Out.WriteLine($"20,000 disabled native trace calls: {allocated} managed bytes.");
                Assert.That(allocated, Is.LessThan(4096), "Disabled logs must not marshal a string per index entry.");
            }
        }
        finally
        {
            ffmpeg.av_log_set_level(previousLevel);
        }
    }
}
