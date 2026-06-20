using Beutl.Helpers;

namespace Beutl.UnitTests.Helpers;

[TestFixture]
public class CancellationTokenSourceHelperTests
{
    [Test]
    public void CancelIgnoringDisposed_CancelsActiveTokenSource()
    {
        using var cts = new CancellationTokenSource();

        CancellationTokenSourceHelper.CancelIgnoringDisposed(cts);

        Assert.That(cts.IsCancellationRequested, Is.True);
    }

    [Test]
    public void CancelIgnoringDisposed_IgnoresDisposedTokenSource()
    {
        using var cts = new CancellationTokenSource();
        cts.Dispose();

        Assert.DoesNotThrow(() => CancellationTokenSourceHelper.CancelIgnoringDisposed(cts));
    }

    [Test]
    public void CancelIgnoringDisposed_NullTokenSource_IsNoOp()
    {
        // The BufferedPlayer call sites pass a volatile field that is frequently null (e.g. _waitTimerToken before
        // any WaitTimer, or after a wait resets it); pin that null is a safe no-op so a later non-conditional Cancel
        // cannot silently regress.
        Assert.DoesNotThrow(() => CancellationTokenSourceHelper.CancelIgnoringDisposed(null));
    }

    [Test]
    public void CancelIgnoringDisposed_AlreadyCancelledTokenSource_StaysCancelled()
    {
        using var cts = new CancellationTokenSource();
        CancellationTokenSourceHelper.CancelIgnoringDisposed(cts);

        Assert.DoesNotThrow(() => CancellationTokenSourceHelper.CancelIgnoringDisposed(cts));
        Assert.That(cts.IsCancellationRequested, Is.True);
    }
}
