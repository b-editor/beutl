using System.Diagnostics;
using Beutl.Configuration;

namespace Beutl.UnitTests.Configuration;

// The fake memory_pressure is a /bin/sh script.
[TestFixture]
[Platform(Exclude = "Win")]
public class SystemMemoryCapacityTests
{
    private static readonly TimeSpan s_testTimeout = TimeSpan.FromSeconds(20);

    [Test]
    public void ReadsTheByteCountFromTheFirstLine()
    {
        ulong bytes = Query("echo 'The system has 17179869184 (4194304 pages with a page size of 4096).'",
            TimeSpan.FromSeconds(10));

        Assert.That(bytes, Is.EqualTo(17179869184UL));
    }

    [Test]
    public void OutputLargerThanThePipeBufferDoesNotDeadlock()
    {
        // A megabyte after the first line fills the pipe long before the child can exit.
        Task<ulong> query = Task.Run(() => Query("echo 'The system has 8589934592'; head -c 1048576 /dev/zero",
            TimeSpan.FromSeconds(10)));

        Assert.That(query.Wait(s_testTimeout), Is.True, "The query deadlocked on a full pipe.");
        Assert.That(query.Result, Is.EqualTo(8589934592UL));
    }

    [Test]
    public void AChildThatNeverExitsFallsBackAfterTheTimeout()
    {
        var stopwatch = Stopwatch.StartNew();
        Task<ulong> query = Task.Run(() => Query("sleep 30", TimeSpan.FromMilliseconds(200)));

        Assert.That(query.Wait(s_testTimeout), Is.True, "The query waited for a child that never exits.");
        Assert.That(query.Result, Is.EqualTo(SystemMemoryCapacity.FallbackBytes));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
    }

    private static ulong Query(string script, TimeSpan timeout)
        => SystemMemoryCapacity.QueryMacMemoryCapacity(new ProcessStartInfo("/bin/sh", ["-c", script]), timeout);
}
