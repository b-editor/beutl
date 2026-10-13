namespace Beutl.UnitTests.TestInfrastructure;

public static class TestWait
{
    // Polls the condition and fails the test when it is still false at the timeout.
    public static async Task UntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True, "condition did not become true within the timeout");
    }
}
