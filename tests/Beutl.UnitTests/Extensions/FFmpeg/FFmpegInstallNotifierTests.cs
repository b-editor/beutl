using Beutl.Extensions.FFmpeg;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegInstallNotifierTests
{
    [SetUp]
    public void ResetState()
    {
        // MarkInstalled clears the throttle slot (Interlocked.Exchange to 0).
        FFmpegInstallNotifier.MarkInstalled();
    }

    [Test]
    public void TryAcquireNotifySlot_FirstCall_Wins()
    {
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000), Is.True);
    }

    [Test]
    public void TryAcquireNotifySlot_SecondCallWithinWindow_Loses()
    {
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000), Is.True);
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000), Is.False);
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 5_000), Is.False);
    }

    [Test]
    public void TryAcquireNotifySlot_AfterWindowElapsed_WinsAgain()
    {
        const long throttleMs = 10_000;
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000), Is.True);
        Assert.That(FFmpegInstallNotifier.TryAcquireNotifySlot(now: 1_000 + throttleMs), Is.True);
    }

    // Regression test for the TOCTOU race: when many threads observe the same
    // pre-throttle state simultaneously, exactly one must acquire the slot.
    // The pre-fix Read/Exchange split allows >=2 winners; the CAS-based fix
    // collapses that to 1 deterministically.
    [Test]
    public void TryAcquireNotifySlot_UnderConcurrency_OnlyOneWinner()
    {
        const int iterations = 25;
        const int threads = 64;

        for (int i = 0; i < iterations; i++)
        {
            FFmpegInstallNotifier.MarkInstalled();

            long now = 1_000_000L + i; // any non-zero value works
            int winners = 0;
            using var barrier = new Barrier(threads);
            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    barrier.SignalAndWait();
                    if (FFmpegInstallNotifier.TryAcquireNotifySlot(now))
                        Interlocked.Increment(ref winners);
                });
            }

            Task.WaitAll(tasks);
            Assert.That(winners, Is.EqualTo(1), $"iteration {i}: expected exactly one winner");
        }
    }
}
