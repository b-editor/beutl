using Beutl.Extensions.FFmpeg;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegWorkerCodecCacheTests
{
    [Test]
    public void MissingCodecNotification_DoesNotHoldCacheLock()
    {
        FFmpegInstallNotifier.MarkInstalled();
        FFmpegWorkerCodecCache.Invalidate();

        using var notificationEntered = new ManualResetEventSlim();
        Task? nestedQuery = null;
        int nestedTimedOut = 0;

        void OnAvailabilityChanged(object? sender, EventArgs e)
        {
            notificationEntered.Set();
            nestedQuery = Task.Run(() =>
            {
                FFmpegWorkerCodecCache.Invalidate();
                _ = FFmpegWorkerCodecCache.GetVideoCodecs();
            });

            if (!nestedQuery.Wait(TimeSpan.FromSeconds(2)))
                Interlocked.Exchange(ref nestedTimedOut, 1);
        }

        FFmpegInstallNotifier.AvailabilityChanged += OnAvailabilityChanged;
        try
        {
            _ = FFmpegWorkerCodecCache.GetVideoCodecs();
            if (!notificationEntered.IsSet)
                Assert.Ignore("FFmpeg libraries are available; missing-codec notification was not raised.");

            Assert.That(Volatile.Read(ref nestedTimedOut), Is.Zero,
                "availability callbacks must not hold the codec-cache lock");
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= OnAvailabilityChanged;
            if (nestedQuery is not null)
                nestedQuery.Wait(TimeSpan.FromSeconds(5));
            FFmpegWorkerCodecCache.Invalidate();
            FFmpegInstallNotifier.MarkInstalled();
        }
    }
}
