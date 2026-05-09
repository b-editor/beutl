using Beutl.Threading;

namespace Beutl.UnitTests.Threading;

public class DispatcherOperationTests
{
    [Test]
    public void Run_InvokesAction()
    {
        bool invoked = false;
        var op = new DispatcherOperation(() => invoked = true, DispatchPriority.Medium, CancellationToken.None);

        op.Run();

        Assert.That(invoked, Is.True);
    }

    [Test]
    public void Run_WithCancelledToken_DoesNotInvokeAction()
    {
        bool invoked = false;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var op = new DispatcherOperation(() => invoked = true, DispatchPriority.Medium, cts.Token);

        op.Run();

        Assert.That(invoked, Is.False);
    }

    [Test]
    public void Properties_PreserveConstructorArguments()
    {
        Action action = () => { };
        var op = new DispatcherOperation(action, DispatchPriority.High, CancellationToken.None);

        Assert.That(op.Action, Is.SameAs(action));
        Assert.That(op.Priority, Is.EqualTo(DispatchPriority.High));
        Assert.That(op.Token, Is.EqualTo(CancellationToken.None));
    }

    [Test]
    public void Run_WhenFlowSuppressed_StillInvokes()
    {
        ExecutionContext.SuppressFlow();
        try
        {
            bool invoked = false;
            var op = new DispatcherOperation(() => invoked = true, DispatchPriority.Low, CancellationToken.None);

            op.Run();

            Assert.That(invoked, Is.True);
        }
        finally
        {
            ExecutionContext.RestoreFlow();
        }
    }
}
