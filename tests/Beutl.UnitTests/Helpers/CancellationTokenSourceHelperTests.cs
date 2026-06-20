using Beutl.Helpers;

namespace Beutl.UnitTests.Helpers;

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
}
