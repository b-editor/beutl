using Beutl.Helpers;

namespace Beutl.UnitTests.Helpers;

[TestFixture]
public class CancellationTokenSourceHelperTests
{
    [Test]
    public void CancelIgnoringDisposed_CancelsActiveTokenSource()
    {
        using var cts = new CancellationTokenSource();

        cts.CancelIgnoringDisposed();

        Assert.That(cts.IsCancellationRequested, Is.True);
    }

    [Test]
    public void CancelIgnoringDisposed_IgnoresDisposedTokenSource()
    {
        using var cts = new CancellationTokenSource();
        cts.Dispose();

        Assert.That(() => cts.CancelIgnoringDisposed(), Throws.Nothing);
    }

    [Test]
    public void CancelIgnoringDisposed_NullTokenSource_IsNoOp()
    {
        // Call sites pass a volatile field that is frequently null.
        Assert.That(() => CancellationTokenSourceHelper.CancelIgnoringDisposed(null), Throws.Nothing);
    }

    [Test]
    public void CancelIgnoringDisposed_AlreadyCancelledTokenSource_StaysCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelIgnoringDisposed();

        Assert.That(() => cts.CancelIgnoringDisposed(), Throws.Nothing);
        Assert.That(cts.IsCancellationRequested, Is.True);
    }
}
