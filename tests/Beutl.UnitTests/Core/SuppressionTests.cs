using Beutl.Editor;

namespace Beutl.UnitTests.Core;

public class SuppressionTests
{
    [Test]
    public void PublishingSuppression_NotSuppressedByDefault()
    {
        Assert.That(PublishingSuppression.IsSuppressed, Is.False);
    }

    [Test]
    public void PublishingSuppression_EnterSetsFlagAndDisposeRestores()
    {
        Assert.That(PublishingSuppression.IsSuppressed, Is.False);

        IDisposable scope = PublishingSuppression.Enter();
        try
        {
            Assert.That(PublishingSuppression.IsSuppressed, Is.True);
        }
        finally
        {
            scope.Dispose();
        }

        Assert.That(PublishingSuppression.IsSuppressed, Is.False);
    }

    [Test]
    public void PublishingSuppression_NestedScopesRequireMatchingDisposes()
    {
        IDisposable outer = PublishingSuppression.Enter();
        IDisposable inner = PublishingSuppression.Enter();

        Assert.That(PublishingSuppression.IsSuppressed, Is.True);

        inner.Dispose();
        Assert.That(PublishingSuppression.IsSuppressed, Is.True);

        outer.Dispose();
        Assert.That(PublishingSuppression.IsSuppressed, Is.False);
    }

    [Test]
    public void PublishingSuppression_DoubleDisposeIsIdempotent()
    {
        IDisposable scope = PublishingSuppression.Enter();
        Assert.That(PublishingSuppression.IsSuppressed, Is.True);

        scope.Dispose();
        scope.Dispose(); // double-dispose must not decrement again

        Assert.That(PublishingSuppression.IsSuppressed, Is.False);
    }

    [Test]
    public void RecordingSuppression_NotSuppressedByDefault()
    {
        Assert.That(RecordingSuppression.IsSuppressed, Is.False);
    }

    [Test]
    public void RecordingSuppression_EnterSetsFlagAndDisposeRestores()
    {
        IDisposable scope = RecordingSuppression.Enter();

        Assert.That(RecordingSuppression.IsSuppressed, Is.True);

        scope.Dispose();
        Assert.That(RecordingSuppression.IsSuppressed, Is.False);
    }

    [Test]
    public void RecordingSuppression_DoubleDisposeIsIdempotent()
    {
        IDisposable scope = RecordingSuppression.Enter();

        scope.Dispose();
        scope.Dispose();

        Assert.That(RecordingSuppression.IsSuppressed, Is.False);
    }

    [Test]
    public async Task PublishingSuppression_AsyncLocal_NotLeakedAcrossThreads()
    {
        using IDisposable scope = PublishingSuppression.Enter();
        Assert.That(PublishingSuppression.IsSuppressed, Is.True);

        bool seenInOtherTask = await Task.Run(() => PublishingSuppression.IsSuppressed);

        // AsyncLocal flows into Task.Run, but the captured snapshot remains true.
        // After we explore a child task and return, parent state must still be true.
        Assert.That(seenInOtherTask, Is.True);
        Assert.That(PublishingSuppression.IsSuppressed, Is.True);
    }
}
