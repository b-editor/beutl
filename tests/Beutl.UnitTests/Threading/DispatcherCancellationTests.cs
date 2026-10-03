using System.Collections.Concurrent;
using Beutl.Threading;

namespace Beutl.UnitTests.Threading;

[TestFixture]
public sealed class DispatcherCancellationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task InvokeAsync_CancelledWhileEnteringExecutionContext_DoesNotRaiseUnhandledException(bool returnsValue)
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        using var cancellation = new CancellationTokenSource();
        var unhandled = new ConcurrentQueue<Exception>();
        dispatcher.UnhandledException += (_, args) =>
        {
            unhandled.Enqueue(args.Exception);
            args.Handled = true;
        };

        var marker = new object();
        var ambient = new AsyncLocal<object?>(args =>
        {
            if (args.ThreadContextChanged && ReferenceEquals(args.CurrentValue, marker) && dispatcher.CheckAccess())
            {
                // DispatcherOperation has already checked the token, but its action has not started.
                cancellation.Cancel();
            }
        });
        bool invoked = false;

        try
        {
            ambient.Value = marker;
            Task operation = returnsValue
                ? dispatcher.InvokeAsync(() => { invoked = true; return 42; }, ct: cancellation.Token)
                : dispatcher.InvokeAsync(() => { invoked = true; }, ct: cancellation.Token);
            ambient.Value = null;

            TaskCanceledException? failure = await Assert.ThrowsAsync<TaskCanceledException>(
                async () => await operation.WaitAsync(TimeSpan.FromSeconds(5)));

            // Drain the preceding operation, including its unhandled-exception notification.
            await dispatcher.InvokeAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
                Assert.That(operation.IsCanceled, Is.True);
                Assert.That(invoked, Is.False);
                Assert.That(unhandled, Is.Empty);
            });
        }
        finally
        {
            ambient.Value = null;
            dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        }
    }
}
