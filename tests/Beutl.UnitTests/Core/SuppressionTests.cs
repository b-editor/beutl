using Beutl.Editor;

namespace Beutl.UnitTests.Core;

public class SuppressionTests
{
    // Both wrappers only delegate to an AsyncSuppressionCounter of their own.
    private static IEnumerable<TestCaseData> Suppressions()
    {
        yield return new TestCaseData(
                (Func<IDisposable>)PublishingSuppression.Enter,
                (Func<bool>)(() => PublishingSuppression.IsSuppressed))
            .SetArgDisplayNames(nameof(PublishingSuppression));
        yield return new TestCaseData(
                (Func<IDisposable>)HistoryReplay.Enter,
                (Func<bool>)(() => HistoryReplay.IsActive))
            .SetArgDisplayNames(nameof(HistoryReplay));
    }

    [TestCaseSource(nameof(Suppressions))]
    public void EnterSetsFlagAndDisposeRestores(Func<IDisposable> enter, Func<bool> isSuppressed)
    {
        Assert.That(isSuppressed(), Is.False);

        IDisposable scope = enter();
        try
        {
            Assert.That(isSuppressed(), Is.True);
        }
        finally
        {
            scope.Dispose();
        }

        Assert.That(isSuppressed(), Is.False);
    }

    [TestCaseSource(nameof(Suppressions))]
    public void NestedScopesRequireMatchingDisposes(Func<IDisposable> enter, Func<bool> isSuppressed)
    {
        IDisposable outer = enter();
        IDisposable inner = enter();

        Assert.That(isSuppressed(), Is.True);

        inner.Dispose();
        Assert.That(isSuppressed(), Is.True);

        outer.Dispose();
        Assert.That(isSuppressed(), Is.False);
    }

    [TestCaseSource(nameof(Suppressions))]
    public void DoubleDisposeIsIdempotent(Func<IDisposable> enter, Func<bool> isSuppressed)
    {
        IDisposable outer = enter();
        IDisposable inner = enter();

        inner.Dispose();
        inner.Dispose(); // double-dispose must not decrement again

        Assert.That(isSuppressed(), Is.True);

        outer.Dispose();
        Assert.That(isSuppressed(), Is.False);
    }

    [Test]
    public async Task PublishingSuppression_AsyncLocal_FlowsIntoChildTaskAndPreservesParent()
    {
        using IDisposable scope = PublishingSuppression.Enter();
        Assert.That(PublishingSuppression.IsSuppressed, Is.True);

        bool seenInOtherTask = await Task.Run(() => PublishingSuppression.IsSuppressed);

        Assert.That(seenInOtherTask, Is.True);
        Assert.That(PublishingSuppression.IsSuppressed, Is.True);
    }
}
