using Beutl.Threading;

namespace Beutl.UnitTests.Threading;

[TestFixture]
public sealed class QueueSynchronizationContextTests
{
    [Test]
    public async Task Send_FromDispatcherThread_RunsSynchronously()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                SynchronizationContext context = SynchronizationContext.Current!;
                var state = new object();
                bool invoked = false;

                context.Send(received =>
                {
                    Assert.That(dispatcher.CheckAccess(), Is.True);
                    Assert.That(received, Is.SameAs(state));
                    invoked = true;
                }, state);

                Assert.That(invoked, Is.True);
            }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            dispatcher.Shutdown();
        }

        Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
    }

    [Test]
    public async Task Send_FromDispatcherThread_PropagatesCallbackFailure()
    {
        var dispatcher = Dispatcher.Spawn();
        dispatcher.Thread.IsBackground = true;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                SynchronizationContext context = SynchronizationContext.Current!;
                var failure = new InvalidOperationException("Callback failed.");

                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                    () => context.Send(_ => throw failure, null));

                Assert.That(actual, Is.SameAs(failure));
            }).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            dispatcher.Shutdown();
        }

        Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
    }
}
